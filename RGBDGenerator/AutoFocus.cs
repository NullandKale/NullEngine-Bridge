using ILGPU.Runtime;
using ILGPU;
using ILGPU.Algorithms;
using GPU;
using RGBDGenerator;
using System.Drawing;
using LKG_NVIDIA_RAYS.Utils;

namespace GPU
{
    public static partial class Kernels
    {
        /// <summary>
        /// Kernel to analyze a depth map and determine optimal focal point.
        /// This implementation uses a weighted histogram approach to find the most prominent
        /// depths while giving preference to central regions and avoiding background/foreground extremes.
        /// </summary>
        /// <param name="index">Thread index</param>
        /// <param name="depthMap">Input depth map</param>
        /// <param name="width">Width of depth map</param>
        /// <param name="height">Height of depth map</param>
        /// <param name="histogram">Output histogram (typically 256 bins)</param>
        /// <param name="faceBoxes">Optional detected face boxes (x, y, width, height, confidence) - each face uses 5 values</param>
        /// <param name="numFaces">Number of detected faces</param>
        /// <param name="useFaces">Whether to prioritize faces (1) or not (0)</param>
        public static void AnalyzeDepthForAutoFocus(
            Index1D index,
            ArrayView<float> depthMap,
            int width,
            int height,
            ArrayView<int> histogram)
        {
            // Clear the histogram bin if this is an active thread for a bin
            if (index < histogram.Length)
            {
                histogram[index] = 0;
            }

            // Synchronize to ensure all histogram bins are cleared
            Group.Barrier();

            // Only process valid pixel positions
            if (index >= width * height)
                return;

            int x = index % width;
            int y = index / width;
            float depth = depthMap[index];

            // Skip invalid depth values
            if (depth <= 0.0f)
                return;

            // Normalize pixel coordinates to [0,1] for weighted sampling
            float normalizedX = x / (float)width;
            float normalizedY = y / (float)height;

            // Calculate distance from center (0,0 is top-left, 0.5,0.5 is center)
            float centerDistX = XMath.Abs(normalizedX - 0.5f);
            float centerDistY = XMath.Abs(normalizedY - 0.5f);
            float centerDist = XMath.Sqrt(centerDistX * centerDistX + centerDistY * centerDistY);

            // Center weighting: pixels closer to center get higher weight (max 1.0)
            float centerWeight = 1.0f - XMath.Min(centerDist * 1.5f, 0.9f); // 1.0 at center, 0.1 at corners

            // Initialize our final weight
            float weight = centerWeight;
      
            // Map the depth value to a histogram bin (assuming depth is normalized to [0,1])
            int bin = (int)(depth * (histogram.Length - 1));

            // Clamp bin to valid range
            bin = (int)(XMath.Max(0, XMath.Min(bin, histogram.Length - 1)));

            // Convert weight to integer contribution (scale up for better precision)
            int contribution = (int)(weight * 1000.0f);

            // Use atomic add to update the histogram bin safely
            Atomic.Add(ref histogram[bin], contribution);
        }

        /// <summary>
        /// Kernel to remap depth values based on an auto-focus target depth.
        /// This will shift the focus plane to 0.5 based on the autofocus strength.
        /// </summary>
        /// <param name="index">Thread index</param>
        /// <param name="depthMap">Input/output depth map</param>
        /// <param name="focusDepth">Target depth to focus on</param>
        /// <param name="autoFocusStrength">Strength of auto-focus effect (0.0-1.0)</param>
        /// <param name="depthMinMax">Array with [min, max] depth values</param>
        public static void RemapDepthForAutoFocus(
            Index1D index,
            ArrayView<float> depthMap,
            float focusDepth,
            float autoFocusStrength,
            ArrayView<float> depthMinMax)
        {
            if (index >= depthMap.Length)
                return;

            float depth = depthMap[index];

            // Skip invalid depth values
            if (depth <= 0.0f)
            {
                return;
            }

            // Get the min/max depth for normalization
            float minDepth = depthMinMax[0];
            float maxDepth = depthMinMax[1];
            float depthRange = maxDepth - minDepth;

            if (depthRange <= 1e-6f)
            {
                // Avoid division by zero if the depth range is too small
                depthMap[index] = 0.5f;
                return;
            }

            // Normalize the depth to [0,1] range
            float normalizedDepth = (depth - minDepth) / depthRange;

            // Calculate how much to shift by (based on strength)
            float targetNormalizedFocus = (focusDepth - minDepth) / depthRange;
            float shift = (0.5f - targetNormalizedFocus) * autoFocusStrength;

            // Apply the shift and blend based on strength
            float remappedDepth = normalizedDepth + shift;

            // Optional: Apply a non-linear compression around the focus point
            // This gives more detail near the focus plane and less in far background/foreground
            if (autoFocusStrength > 0.0f)
            {
                // Calculate distance from focus plane (normalized)
                float focusDistance = XMath.Abs(normalizedDepth - targetNormalizedFocus);

                // Apply a slight sigmoid curve for compression (stronger at edges)
                float compressionStrength = autoFocusStrength * 0.25f; // Reduce effect to be subtle
                float compression = compressionStrength * (focusDistance * focusDistance);

                // Apply compression: pull values toward the focus plane more aggressively
                // when they're far from it
                if (normalizedDepth < targetNormalizedFocus)
                {
                    remappedDepth += compression; // Pull up values below focus
                }
                else
                {
                    remappedDepth -= compression; // Pull down values above focus
                }
            }

            // Clamp result to [0,1]
            remappedDepth = XMath.Max(0.0f, XMath.Min(1.0f, remappedDepth));

            // Convert back to original depth range
            depthMap[index] = remappedDepth * depthRange + minDepth;
        }

        // Find the significant peaks (local maxima)
        const int MinPeakHeight = 100; // Minimum contribution to consider a peak
        const int MinPeakDistance = 10; // Minimum bins between peaks
        struct Peak { public int position; public int height; }

        /// <summary>
        /// Helper kernel to find the optimal focus depth from a histogram.
        /// This finds the weighted center of the most significant histogram peak.
        /// </summary>
        /// <param name="index">Thread index (unused, this is a single-thread kernel)</param>
        /// <param name="histogram">Input histogram</param>
        /// <param name="focusResult">Output array: [0]=focus depth, [1]=confidence</param>
        public static void FindOptimalFocusDepth(
            Index1D index,
            ArrayView<int> histogram,
            ArrayView<float> focusResult)
        {
            if (index > 0) return; // Only use the first thread

            int histogramSize = (int)histogram.Length;

            // First, smooth the histogram to reduce noise
            int[] smoothed = new int[histogramSize];
            for (int i = 0; i < histogramSize; i++)
            {
                int sum = 0;
                int count = 0;

                // Apply a 5-bin average for smoothing
                for (int j = i - 2; j <= i + 2; j++)
                {
                    if (j >= 0 && j < histogramSize)
                    {
                        sum += histogram[j];
                        count++;
                    }
                }

                smoothed[i] = count > 0 ? sum / count : 0;
            }


            Peak[] peaks = new Peak[20]; // Up to 20 peaks
            int peakCount = 0;

            for (int i = 2; i < histogramSize - 2; i++)
            {
                int current = smoothed[i];

                // Check if this is a local maximum
                if (current > MinPeakHeight &&
                    current > smoothed[i - 2] && current > smoothed[i - 1] &&
                    current > smoothed[i + 1] && current > smoothed[i + 2])
                {
                    // Check if it's far enough from existing peaks
                    bool farEnough = true;
                    for (int p = 0; p < peakCount; p++)
                    {
                        if (XMath.Abs(i - peaks[p].position) < MinPeakDistance)
                        {
                            farEnough = false;
                            // If this peak is higher than the existing one, replace it
                            if (current > peaks[p].height)
                            {
                                peaks[p].position = i;
                                peaks[p].height = current;
                            }
                            break;
                        }
                    }

                    // Add as a new peak if far enough and we have space
                    if (farEnough && peakCount < peaks.Length)
                    {
                        peaks[peakCount].position = i;
                        peaks[peakCount].height = current;
                        peakCount++;
                    }
                }
            }

            // If no significant peaks were found, use the bin with maximum value
            if (peakCount == 0)
            {
                int maxBin = 0;
                int maxValue = 0;

                for (int i = 0; i < histogramSize; i++)
                {
                    if (smoothed[i] > maxValue)
                    {
                        maxValue = smoothed[i];
                        maxBin = i;
                    }
                }

                // Use max bin as focus depth
                focusResult[0] = maxBin / (float)(histogramSize - 1);
                focusResult[1] = 0.5f; // Medium confidence
                return;
            }

            // Sort peaks by height (descending)
            for (int i = 0; i < peakCount - 1; i++)
            {
                for (int j = i + 1; j < peakCount; j++)
                {
                    if (peaks[i].height < peaks[j].height)
                    {
                        Peak temp = peaks[i];
                        peaks[i] = peaks[j];
                        peaks[j] = temp;
                    }
                }
            }

            // Calculate weighted average of the top 2 peaks (or just 1 if that's all we have)
            int peaksToUse = XMath.Min(2, peakCount);
            float weightedSum = 0;
            int totalWeight = 0;

            for (int i = 0; i < peaksToUse; i++)
            {
                weightedSum += peaks[i].position * peaks[i].height;
                totalWeight += peaks[i].height;
            }

            // Calculate normalized focus depth
            float focusDepth = weightedSum / (totalWeight * (histogramSize - 1));

            // Calculate confidence based on peak sharpness
            float confidence = (float)peaks[0].height / (totalWeight + 1);

            // Store results
            focusResult[0] = focusDepth;
            focusResult[1] = confidence;
        }
    }
}