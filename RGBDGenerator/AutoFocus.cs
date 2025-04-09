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
        /// Kernel to analyze a depth map and determine optimal focal point by filling a histogram.
        /// </summary>
        /// <param name="index">Thread index</param>
        /// <param name="depthMap">Input depth map</param>
        /// <param name="width">Width of depth map</param>
        /// <param name="height">Height of depth map</param>
        /// <param name="histogram">Output histogram (256 bins)</param>
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

            // Distance from center
            float centerDistX = XMath.Abs(normalizedX - 0.5f);
            float centerDistY = XMath.Abs(normalizedY - 0.5f);
            float centerDist = XMath.Sqrt(centerDistX * centerDistX + centerDistY * centerDistY);

            // Weight: near center => bigger. We'll store up to 1000
            float centerWeight = 1.0f - XMath.Min(centerDist * 1.5f, 0.9f);
            float weight = centerWeight;

            // Map depth => histogram bin
            int bin = (int)(depth * (histogram.Length - 1));
            bin = XMath.Clamp(bin, 0, (int)histogram.Length - 1);

            // Convert weight => integer contribution
            int contribution = (int)(weight * 1000.0f);
            Atomic.Add(ref histogram[bin], contribution);
        }

        /// <summary>
        /// Kernel to remap depth values based on an auto-focus target depth.
        /// This shifts the focus plane to 0.5 based on the autofocus strength.
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
                return;

            float minDepth = depthMinMax[0];
            float maxDepth = depthMinMax[1];
            float depthRange = maxDepth - minDepth;

            if (depthRange <= 1e-6f)
            {
                depthMap[index] = 0.5f;
                return;
            }

            // Normalize
            float normalizedDepth = (depth - minDepth) / depthRange;

            // Shift so that focusDepth => 0.5
            float targetNormalizedFocus = (focusDepth - minDepth) / depthRange;
            float shift = (0.5f - targetNormalizedFocus) * autoFocusStrength;
            float remappedDepth = normalizedDepth + shift;

            // Optional compression near focus
            if (autoFocusStrength > 0.0f)
            {
                float focusDistance = XMath.Abs(normalizedDepth - targetNormalizedFocus);
                float compressionStrength = autoFocusStrength * 0.25f;
                float compression = compressionStrength * (focusDistance * focusDistance);

                if (normalizedDepth < targetNormalizedFocus)
                    remappedDepth += compression;
                else
                    remappedDepth -= compression;
            }

            // Clamp to [0,1], then back to original range
            remappedDepth = XMath.Clamp(remappedDepth, 0f, 1f);
            depthMap[index] = remappedDepth * depthRange + minDepth;
        }
    }

    /// <summary>
    /// Encapsulates GPU-based histogram building + CPU-based auto-focus calculations
    /// for depth. The DepthGenerator can create an instance of this and call
    /// its methods to gather depth stats, compute a focus depth, and remap
    /// the depth buffer.
    /// </summary>
    public sealed class DepthAutoFocusAndStats : IDisposable
    {
        private readonly Accelerator _device;

        // GPU kernel for building histogram
        public Action<Index1D, ArrayView<float>, int, int, ArrayView<int>> AnalyzeDepthKernel;
        // GPU kernel for remapping depths once we have our focus
        public Action<Index1D, ArrayView<float>, float, float, ArrayView<float>> RemapDepthKernel;

        // GPU buffers
        public MemoryBuffer1D<int, Stride1D.Dense> DepthHistogram { get; private set; }
        public MemoryBuffer1D<float, Stride1D.Dense> DepthMinMax { get; private set; }

        private const int HistogramBins = 256;

        // We'll replicate the same constants from the GPU side for local maxima
        private const int MinPeakHeight = 100;
        private const int MinPeakDistance = 10;

        public DepthAutoFocusAndStats(Accelerator device)
        {
            _device = device;

            // Load the needed kernels
            AnalyzeDepthKernel = _device.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, int, int, ArrayView<int>>(
                Kernels.AnalyzeDepthForAutoFocus);

            RemapDepthKernel = _device.LoadAutoGroupedStreamKernel<
                Index1D, ArrayView<float>, float, float, ArrayView<float>>(
                Kernels.RemapDepthForAutoFocus);

            // Allocate GPU buffers
            DepthHistogram = _device.Allocate1D<int>(HistogramBins);
            DepthMinMax = _device.Allocate1D<float>(2); // [0] = minDepth, [1] = maxDepth
        }

        /// <summary>
        /// Builds the histogram on the GPU, then copies it to CPU and uses
        /// single-threaded logic to find the best focus depth.
        /// Then uses that focus to remap depth values (still on the GPU).
        /// </summary>
        /// <param name="depthFloats">CPU depth buffer</param>
        /// <param name="width">Depth image width</param>
        /// <param name="height">Depth image height</param>
        /// <param name="lastFocusDepth">Focus from previous frame (for smoothing)</param>
        /// <param name="focusSmoothing">Blend factor [0..1], e.g. 0.8 => mostly old focus</param>
        /// <param name="autoFocusStrength">Strength of effect [0..1]</param>
        /// <returns>Newly computed focus depth after smoothing</returns>
        public float ApplyAutoFocus(
            float[] depthFloats,
            int width,
            int height,
            float lastFocusDepth,
            float focusSmoothing,
            float autoFocusStrength)
        {
            int totalPixels = depthFloats.Length;
            if (totalPixels <= 0)
                return lastFocusDepth;

            // (1) CPU min/max
            float minDepth = float.MaxValue;
            float maxDepth = float.MinValue;
            for (int i = 0; i < totalPixels; i++)
            {
                float d = depthFloats[i];
                if (d <= 0f) // skip invalid
                    continue;

                if (d < minDepth) minDepth = d;
                if (d > maxDepth) maxDepth = d;
            }
            if (minDepth > maxDepth)
            {
                // No valid depth
                return lastFocusDepth;
            }

            // (2) Copy depth to GPU
            using var depthBuffer = _device.Allocate1D<float>(totalPixels);
            depthBuffer.CopyFromCPU(depthFloats);

            // (3) Copy min/max to GPU
            float[] minMax = new float[] { minDepth, maxDepth };
            DepthMinMax.CopyFromCPU(minMax);

            // (4) Build histogram on GPU
            DepthHistogram.MemSetToZero();
            AnalyzeDepthKernel(totalPixels, depthBuffer.View, width, height, DepthHistogram.View);
            _device.Synchronize();

            // (5) Copy histogram back to CPU and do single-threaded logic
            int[] histogramCPU = new int[HistogramBins];
            DepthHistogram.CopyToCPU(histogramCPU);

            float rawFocusDepth = FindBestFocusDepthCPU(histogramCPU);
            // optionally you could compute a "confidence" but not used here

            // (6) Smooth with old focus
            float newFocusDepth = lastFocusDepth * focusSmoothing +
                                  rawFocusDepth * (1f - focusSmoothing);

            // (7) Remap depth on GPU
            RemapDepthKernel(totalPixels, depthBuffer.View, newFocusDepth, autoFocusStrength, DepthMinMax.View);
            _device.Synchronize();

            // (8) Copy updated depth back to CPU
            depthBuffer.CopyToCPU(depthFloats);

            return newFocusDepth;
        }

        /// <summary>
        /// CPU method replicating local maxima detection & smoothing logic
        /// from the old GPU kernel, but done single-threaded on the CPU.
        /// </summary>
        private float FindBestFocusDepthCPU(int[] histogram)
        {
            int histogramSize = histogram.Length;

            // Smooth
            int[] smoothed = new int[histogramSize];
            for (int i = 0; i < histogramSize; i++)
            {
                int sum = 0, count = 0;
                for (int j = i - 2; j <= i + 2; j++)
                {
                    if (j >= 0 && j < histogramSize)
                    {
                        sum += histogram[j];
                        count++;
                    }
                }
                smoothed[i] = (count > 0) ? (sum / count) : 0;
            }

            // Find local maxima
            var peaks = new System.Collections.Generic.List<(int pos, int height)>(20);

            for (int i = 2; i < histogramSize - 2; i++)
            {
                int current = smoothed[i];
                if (current > MinPeakHeight &&
                    current > smoothed[i - 2] && current > smoothed[i - 1] &&
                    current > smoothed[i + 1] && current > smoothed[i + 2])
                {
                    bool farEnough = true;

                    // check distance from existing peaks
                    for (int p = 0; p < peaks.Count; p++)
                    {
                        if (System.Math.Abs(i - peaks[p].pos) < MinPeakDistance)
                        {
                            farEnough = false;
                            // if this peak is higher, replace
                            if (current > peaks[p].height)
                            {
                                peaks[p] = (i, current);
                            }
                            break;
                        }
                    }

                    if (farEnough && peaks.Count < 20)
                    {
                        peaks.Add((i, current));
                    }
                }
            }

            // If no peaks, pick the bin with the largest value
            if (peaks.Count == 0)
            {
                int maxBin = 0, maxValue = 0;
                for (int i = 0; i < histogramSize; i++)
                {
                    int val = smoothed[i];
                    if (val > maxValue)
                    {
                        maxValue = val;
                        maxBin = i;
                    }
                }
                return maxBin / (float)(histogramSize - 1);
            }

            // Sort by descending height
            peaks.Sort((a, b) => b.height.CompareTo(a.height));

            // Weighted average of top 2 peaks
            int peaksToUse = System.Math.Min(2, peaks.Count);
            float weightedSum = 0;
            int totalWeight = 0;

            for (int i = 0; i < peaksToUse; i++)
            {
                weightedSum += peaks[i].pos * peaks[i].height;
                totalWeight += peaks[i].height;
            }

            float focusDepth = weightedSum / (totalWeight * (histogramSize - 1));
            return focusDepth;
        }

        /// <summary>
        /// Optional CPU-based pass to refine focus using face boxes or other logic.
        /// (Single-threaded approach.)
        /// </summary>
        public float RefineFocusDepthWithFaces(
            float[] depthFloats,
            float[] faceBoxes,
            int numFaces,
            int width,
            int height,
            float currentFocusDepth)
        {
            if (numFaces <= 0)
                return currentFocusDepth;

            float totalFaceDepth = 0f;
            float totalWeight = 0f;

            for (int i = 0; i < numFaces; i++)
            {
                int baseIdx = i * 5;
                float x = faceBoxes[baseIdx + 0];
                float y = faceBoxes[baseIdx + 1];
                float w = faceBoxes[baseIdx + 2];
                float h = faceBoxes[baseIdx + 3];
                float conf = faceBoxes[baseIdx + 4];

                int cx = (int)(x + w * 0.5f);
                int cy = (int)(y + h * 0.5f);

                if (cx < 0 || cy < 0 || cx >= width || cy >= height)
                    continue;

                float faceDepth = depthFloats[cy * width + cx];
                if (faceDepth > 0f)
                {
                    totalFaceDepth += faceDepth * conf;
                    totalWeight += conf;
                }
            }

            if (totalWeight <= 0f)
                return currentFocusDepth;

            float avgFaceDepth = totalFaceDepth / totalWeight;
            // Example: move halfway toward face depth
            return 0.5f * currentFocusDepth + 0.5f * avgFaceDepth;
        }

        public void Dispose()
        {
            DepthHistogram?.Dispose();
            DepthMinMax?.Dispose();
        }
    }
}
