using ILGPU.Runtime;
using ILGPU;
using ILGPU.Algorithms;

namespace GPU
{
    // Host-side circular buffer holding 20 frames.
    public class DepthRollingWindow : System.IDisposable
    {
        public MemoryBuffer1D<float, Stride1D.Dense> Frame0;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame1;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame2;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame3;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame4;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame5;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame6;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame7;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame8;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame9;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame10;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame11;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame12;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame13;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame14;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame15;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame16;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame17;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame18;
        public MemoryBuffer1D<float, Stride1D.Dense> Frame19;

        // Current insertion index into the circular buffer.
        public int CurrentIndex;
        public int frameWidth;
        public int frameHeight;

        /// <summary>
        /// Controls the sensitivity to edge detection in the depth map.
        /// - Higher values (7.0-10.0): Fewer edges detected, resulting in smoother filtering across edges.
        /// - Lower values (1.0-3.0): More edges detected, preserving more edge details and reducing ghosting.
        /// Reasonable range: 1.0-10.0
        /// </summary>
        public float EdgeThreshold { get; set; } = 5.0f;

        /// <summary>
        /// Controls how much depth change between frames is considered motion.
        /// - Higher values (6.0-8.0): Less sensitive to motion, more temporal smoothing but potential ghosting.
        /// - Lower values (1.0-3.0): More sensitive to motion, less ghosting but more flicker may remain.
        /// Reasonable range: 1.0-8.0
        /// </summary>
        public float MotionThreshold { get; set; } = 5.0f;

        /// <summary>
        /// Controls how quickly older frames lose influence.
        /// - Higher values (3.0-5.0): Slower falloff, more frames contribute significantly to filtering.
        /// - Lower values (1.0-2.0): Rapid falloff, recent frames dominate the filtering result.
        /// Reasonable range: 1.0-5.0 (in frames)
        /// </summary>
        public float TemporalDecay { get; set; } = 6.5f;

        /// <summary>
        /// Defines when two depth values are considered similar enough for direct blending.
        /// - Higher values (3.0-5.0): More tolerant of depth differences, smoother but potential ghosting.
        /// - Lower values (0.5-1.5): Stricter matching, less ghosting but potential flicker.
        /// Reasonable range: 0.5-5.0
        /// </summary>
        public float SimilarityDelta { get; set; } = 2.0f;

        /// <summary>
        /// Controls falloff for depth differences beyond SimilarityDelta.
        /// - Higher values (7.0-10.0): Gradual falloff, more blending even with larger differences.
        /// - Lower values (1.0-3.0): Steep falloff, sharp separation between different depths.
        /// Reasonable range: 1.0-10.0
        /// </summary>
        public float SimilaritySigma { get; set; } = 3.0f;

        /// <summary>
        /// Controls how much temporal variance triggers fallback to current frame.
        /// - Higher values (3.0-5.0): More temporal filtering even with high variance.
        /// - Lower values (0.5-1.5): Quickly revert to current frame when variance is detected.
        /// Reasonable range: 0.5-5.0
        /// </summary>
        public float VarianceThreshold { get; set; } = 2.5f;

        /// <summary>
        /// Defines the size of the spatial neighborhood for sampling.
        /// - Higher values (2.0-3.0): Larger neighborhood (5x5 or 7x7), more blur but better stability.
        /// - Lower values (0.5-1.0): Smaller neighborhood (3x3), sharper details but less stability.
        /// Reasonable range: 0.5-3.0 (in pixels)
        /// </summary>
        public float SpatialTemporalRadius { get; set; } = 2.0f;

        // Allocate 20 frames, each of size 'frameSize' (for example, totalPixels).
        public DepthRollingWindow(Accelerator device, int frameWidth, int frameHeight, int frameSize)
        {
            this.frameWidth = frameWidth;
            this.frameHeight = frameHeight;

            Frame0 = device.Allocate1D<float>(frameSize);
            Frame1 = device.Allocate1D<float>(frameSize);
            Frame2 = device.Allocate1D<float>(frameSize);
            Frame3 = device.Allocate1D<float>(frameSize);
            Frame4 = device.Allocate1D<float>(frameSize);
            Frame5 = device.Allocate1D<float>(frameSize);
            Frame6 = device.Allocate1D<float>(frameSize);
            Frame7 = device.Allocate1D<float>(frameSize);
            Frame8 = device.Allocate1D<float>(frameSize);
            Frame9 = device.Allocate1D<float>(frameSize);
            Frame10 = device.Allocate1D<float>(frameSize);
            Frame11 = device.Allocate1D<float>(frameSize);
            Frame12 = device.Allocate1D<float>(frameSize);
            Frame13 = device.Allocate1D<float>(frameSize);
            Frame14 = device.Allocate1D<float>(frameSize);
            Frame15 = device.Allocate1D<float>(frameSize);
            Frame16 = device.Allocate1D<float>(frameSize);
            Frame17 = device.Allocate1D<float>(frameSize);
            Frame18 = device.Allocate1D<float>(frameSize);
            Frame19 = device.Allocate1D<float>(frameSize);
            CurrentIndex = 0;
        }

        // Copy the new depth frame into the current slot and update the circular index.
        public void AddFrame(MemoryBuffer1D<float, Stride1D.Dense> newFrame)
        {
            switch (CurrentIndex)
            {
                case 0: Frame0.CopyFrom(newFrame); break;
                case 1: Frame1.CopyFrom(newFrame); break;
                case 2: Frame2.CopyFrom(newFrame); break;
                case 3: Frame3.CopyFrom(newFrame); break;
                case 4: Frame4.CopyFrom(newFrame); break;
                case 5: Frame5.CopyFrom(newFrame); break;
                case 6: Frame6.CopyFrom(newFrame); break;
                case 7: Frame7.CopyFrom(newFrame); break;
                case 8: Frame8.CopyFrom(newFrame); break;
                case 9: Frame9.CopyFrom(newFrame); break;
                case 10: Frame10.CopyFrom(newFrame); break;
                case 11: Frame11.CopyFrom(newFrame); break;
                case 12: Frame12.CopyFrom(newFrame); break;
                case 13: Frame13.CopyFrom(newFrame); break;
                case 14: Frame14.CopyFrom(newFrame); break;
                case 15: Frame15.CopyFrom(newFrame); break;
                case 16: Frame16.CopyFrom(newFrame); break;
                case 17: Frame17.CopyFrom(newFrame); break;
                case 18: Frame18.CopyFrom(newFrame); break;
                case 19: Frame19.CopyFrom(newFrame); break;
            }
            CurrentIndex = (CurrentIndex + 1) % 20;
        }

        // Create a device-friendly structure for the kernel.
        public dDepthRollingWindow ToDevice()
        {
            var result = new dDepthRollingWindow(
                CurrentIndex, frameWidth, frameHeight,
                Frame0, Frame1, Frame2, Frame3, Frame4,
                Frame5, Frame6, Frame7, Frame8, Frame9,
                Frame10, Frame11, Frame12, Frame13, Frame14,
                Frame15, Frame16, Frame17, Frame18, Frame19);

            // Set filter parameters
            result.edgeThreshold = EdgeThreshold;
            result.motionThreshold = MotionThreshold;
            result.temporalDecay = TemporalDecay;
            result.similarityDelta = SimilarityDelta;
            result.similaritySigma = SimilaritySigma;
            result.varianceThreshold = VarianceThreshold;
            result.spatialTemporalRadius = SpatialTemporalRadius;

            return result;
        }

        public void Dispose()
        {
            Frame0.Dispose();
            Frame1.Dispose();
            Frame2.Dispose();
            Frame3.Dispose();
            Frame4.Dispose();
            Frame5.Dispose();
            Frame6.Dispose();
            Frame7.Dispose();
            Frame8.Dispose();
            Frame9.Dispose();
            Frame10.Dispose();
            Frame11.Dispose();
            Frame12.Dispose();
            Frame13.Dispose();
            Frame14.Dispose();
            Frame15.Dispose();
            Frame16.Dispose();
            Frame17.Dispose();
            Frame18.Dispose();
            Frame19.Dispose();
        }
    }

    // Device-side structure that packages the rolling window for the kernel.
    public struct dDepthRollingWindow
    {
        // The current insertion index from the host.
        public int index;
        public int frameWidth;
        public int frameHeight;
        public float edgeThreshold;
        public float motionThreshold;
        public float temporalDecay;
        public float similarityDelta;
        public float similaritySigma;
        public float varianceThreshold;
        public float spatialTemporalRadius;
        public ArrayView1D<float, Stride1D.Dense> Frame0;
        public ArrayView1D<float, Stride1D.Dense> Frame1;
        public ArrayView1D<float, Stride1D.Dense> Frame2;
        public ArrayView1D<float, Stride1D.Dense> Frame3;
        public ArrayView1D<float, Stride1D.Dense> Frame4;
        public ArrayView1D<float, Stride1D.Dense> Frame5;
        public ArrayView1D<float, Stride1D.Dense> Frame6;
        public ArrayView1D<float, Stride1D.Dense> Frame7;
        public ArrayView1D<float, Stride1D.Dense> Frame8;
        public ArrayView1D<float, Stride1D.Dense> Frame9;
        public ArrayView1D<float, Stride1D.Dense> Frame10;
        public ArrayView1D<float, Stride1D.Dense> Frame11;
        public ArrayView1D<float, Stride1D.Dense> Frame12;
        public ArrayView1D<float, Stride1D.Dense> Frame13;
        public ArrayView1D<float, Stride1D.Dense> Frame14;
        public ArrayView1D<float, Stride1D.Dense> Frame15;
        public ArrayView1D<float, Stride1D.Dense> Frame16;
        public ArrayView1D<float, Stride1D.Dense> Frame17;
        public ArrayView1D<float, Stride1D.Dense> Frame18;
        public ArrayView1D<float, Stride1D.Dense> Frame19;

        public dDepthRollingWindow(
            int index, int frameWidth, int frameHeight,
            ArrayView1D<float, Stride1D.Dense> frame0,
            ArrayView1D<float, Stride1D.Dense> frame1,
            ArrayView1D<float, Stride1D.Dense> frame2,
            ArrayView1D<float, Stride1D.Dense> frame3,
            ArrayView1D<float, Stride1D.Dense> frame4,
            ArrayView1D<float, Stride1D.Dense> frame5,
            ArrayView1D<float, Stride1D.Dense> frame6,
            ArrayView1D<float, Stride1D.Dense> frame7,
            ArrayView1D<float, Stride1D.Dense> frame8,
            ArrayView1D<float, Stride1D.Dense> frame9,
            ArrayView1D<float, Stride1D.Dense> frame10,
            ArrayView1D<float, Stride1D.Dense> frame11,
            ArrayView1D<float, Stride1D.Dense> frame12,
            ArrayView1D<float, Stride1D.Dense> frame13,
            ArrayView1D<float, Stride1D.Dense> frame14,
            ArrayView1D<float, Stride1D.Dense> frame15,
            ArrayView1D<float, Stride1D.Dense> frame16,
            ArrayView1D<float, Stride1D.Dense> frame17,
            ArrayView1D<float, Stride1D.Dense> frame18,
            ArrayView1D<float, Stride1D.Dense> frame19)
        {
            this.index = index;
            this.frameWidth = frameWidth;
            this.frameHeight = frameHeight;
            edgeThreshold = 5.0f;
            motionThreshold = 4.0f;
            temporalDecay = 2.5f;
            similarityDelta = 2.0f;
            similaritySigma = 3.0f;
            varianceThreshold = 1.5f;
            spatialTemporalRadius = 2.0f;

            Frame0 = frame0;
            Frame1 = frame1;
            Frame2 = frame2;
            Frame3 = frame3;
            Frame4 = frame4;
            Frame5 = frame5;
            Frame6 = frame6;
            Frame7 = frame7;
            Frame8 = frame8;
            Frame9 = frame9;
            Frame10 = frame10;
            Frame11 = frame11;
            Frame12 = frame12;
            Frame13 = frame13;
            Frame14 = frame14;
            Frame15 = frame15;
            Frame16 = frame16;
            Frame17 = frame17;
            Frame18 = frame18;
            Frame19 = frame19;
        }

        // Indexer to access a frame by its time distance.
        // rollingWindow[0] returns the most recent frame.
        // rollingWindow[19] returns the least recent frame.
        public ArrayView1D<float, Stride1D.Dense> this[int i]
        {
            get
            {
                // Calculate the physical index: the most recent frame is at (index - 1 + 20) % 20.
                int physicalIndex = (index - 1 - i + 20) % 20;
                switch (physicalIndex)
                {
                    case 0: return Frame0;
                    case 1: return Frame1;
                    case 2: return Frame2;
                    case 3: return Frame3;
                    case 4: return Frame4;
                    case 5: return Frame5;
                    case 6: return Frame6;
                    case 7: return Frame7;
                    case 8: return Frame8;
                    case 9: return Frame9;
                    case 10: return Frame10;
                    case 11: return Frame11;
                    case 12: return Frame12;
                    case 13: return Frame13;
                    case 14: return Frame14;
                    case 15: return Frame15;
                    case 16: return Frame16;
                    case 17: return Frame17;
                    case 18: return Frame18;
                    case 19: return Frame19;
                }
                return default;
            }
        }
    }

    public static partial class Kernels
    {
        // Helper function to sample depth at specific xyz coordinates
        public static float SampleDepth(int pixelX, int pixelY, int frameIdx, dDepthRollingWindow rollingWindow)
        {
            // Boundary checking
            if (pixelX < 0 || pixelX >= rollingWindow.frameWidth ||
                pixelY < 0 || pixelY >= rollingWindow.frameHeight ||
                frameIdx < 0 || frameIdx >= 20)
                return 0.0f;

            return rollingWindow[frameIdx][pixelY * rollingWindow.frameWidth + pixelX];
        }

        // Modified DetectEdges with a 3D search over 20 frames with diminishing temporal weight
        public static float DetectEdges(int x, int y, float currentDepth, dDepthRollingWindow rollingWindow)
        {
            if (currentDepth == 0.0f)
                return 0.0f;

            float maxWeightedDiff = 0.0f;
            // Loop through 20 frames in the t dimension (including current frame at t=0)
            for (int t = 0; t < 20; t++)
            {
                // Compute diminishing weight based on temporal distance.
                float timeWeight = XMath.Exp(-((float)t) / rollingWindow.temporalDecay);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        // Skip the center pixel when t == 0 to avoid comparing the pixel to itself.
                        if (t == 0 && dx == 0 && dy == 0)
                            continue;

                        float neighborDepth = SampleDepth(x + dx, y + dy, t, rollingWindow);
                        if (neighborDepth > 0.0f)
                        {
                            float diff = XMath.Abs(currentDepth - neighborDepth);
                            float weightedDiff = timeWeight * diff;
                            maxWeightedDiff = XMath.Max(maxWeightedDiff, weightedDiff);
                        }
                    }
                }
            }
            // Normalize by the edge threshold and clamp to [0,1]
            return XMath.Min(maxWeightedDiff / rollingWindow.edgeThreshold, 1.0f);
        }

        // Modified DetectMotion with a 3D search over 20 frames with diminishing temporal weight
        public static float DetectMotion(int x, int y, float currentDepth, dDepthRollingWindow rollingWindow)
        {
            if (currentDepth == 0.0f)
                return 0.0f;

            float maxWeightedMotion = 0.0f;
            int validSamples = 0;

            // For motion detection, we start at t=1 (current frame is t=0)
            for (int t = 1; t < 20; t++)
            {
                // Compute diminishing weight based on the frame distance
                float timeWeight = XMath.Exp(-((float)t) / rollingWindow.temporalDecay);
                for (int dy = -1; dy <= 1; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        float neighborDepth = SampleDepth(x + dx, y + dy, t, rollingWindow);
                        if (neighborDepth > 0.0f)
                        {
                            float diff = XMath.Abs(neighborDepth - currentDepth);
                            float weightedDiff = timeWeight * diff;
                            maxWeightedMotion = XMath.Max(maxWeightedMotion, weightedDiff);
                            validSamples++;
                        }
                    }
                }
            }

            if (validSamples == 0)
                return 0.0f;

            // Normalize by the motion threshold, square the result for a non-linear response, and clamp to [0,1]
            float normalizedMotion = maxWeightedMotion / rollingWindow.motionThreshold;
            return XMath.Min(normalizedMotion * normalizedMotion, 1.0f);
        }

        // Improved filtering kernel with focus on flicker reduction while avoiding ghosting.
        // Now also uses varianceThreshold to trigger fallback to the current frame when temporal variance is too high.
        public static void FilterDepthRollingWindow(
            Index1D index,
            dDepthRollingWindow rollingWindow,
            ArrayView<float> output)
        {
            // Convert linear index to 2D coordinates
            int x = index % rollingWindow.frameWidth;
            int y = index / rollingWindow.frameWidth;

            // Current depth value is our starting point
            float currentDepth = SampleDepth(x, y, 0, rollingWindow);

            // Early exit for invalid depths
            if (currentDepth == 0.0f)
            {
                output[index] = 0.0f;
                return;
            }

            // 1. Analyze local characteristics
            float edgeWeight = DetectEdges(x, y, currentDepth, rollingWindow);
            float motionWeight = DetectMotion(x, y, currentDepth, rollingWindow);

            // Combine edge and motion for overall confidence in current frame
            // Higher means trust current frame more
            float currentFrameConfidence = XMath.Max(edgeWeight, motionWeight);

            // 2. Decide on filtering approach based on current frame confidence
            float filteredDepth;

            // A. Fast path for high confidence areas - skip temporal filtering to avoid ghosting
            if (currentFrameConfidence > 0.7f)
            {
                filteredDepth = currentDepth;
            }
            // B. Perform temporal filtering for low to medium confidence areas
            else
            {
                // Parameters for adaptive temporal filtering
                float adaptiveDelta = rollingWindow.similarityDelta * (1.0f + edgeWeight);
                float adaptiveSigma = rollingWindow.similaritySigma * (1.0f + 0.5f * motionWeight);

                // Accumulate weighted depths and weighted squares for variance calculation
                float weightedSum = currentDepth; // Include current frame in sum
                float weightedSquareSum = currentDepth * currentDepth;
                float totalWeight = 1.0f;        // Current frame has base weight of 1

                // Limit of frame history based on confidence
                // Higher confidence = fewer historical frames used
                int maxHistoryFrames = currentFrameConfidence < 0.3f ? 10 : 5;

                // Process historical frames with adaptive weighting
                for (int t = 1; t < maxHistoryFrames; t++)
                {
                    float frameDepth = SampleDepth(x, y, t, rollingWindow);
                    if (frameDepth == 0.0f)
                        continue;

                    // 1. Apply exponential temporal decay
                    float temporalWeight = XMath.Exp(-(float)t / rollingWindow.temporalDecay);

                    // 2. Apply non-linear motion suppression - steep falloff for motion areas
                    if (motionWeight > 0.1f)
                    {
                        temporalWeight *= XMath.Exp(-motionWeight * t * 2.0f);
                    }

                    // 3. Calculate depth similarity weight
                    float depthDiff = XMath.Abs(frameDepth - currentDepth);
                    float similarityWeight;

                    // Apply adaptive thresholding based on local characteristics
                    if (depthDiff < adaptiveDelta)
                    {
                        // For small differences, use weight close to 1
                        similarityWeight = 1.0f - (depthDiff / adaptiveDelta) * 0.2f;
                    }
                    else
                    {
                        // For larger differences, drop off exponentially
                        similarityWeight = 0.8f * XMath.Exp(-(depthDiff - adaptiveDelta) / adaptiveSigma);
                    }

                    // 4. Combine weights for this frame
                    float frameWeight = temporalWeight * similarityWeight;

                    // 5. Add contribution from spatial neighborhood if appropriate
                    if (t <= 3 && currentFrameConfidence < 0.4f && depthDiff < adaptiveDelta * 1.5f)
                    {
                        int radius = (int)rollingWindow.spatialTemporalRadius;
                        float spatialBoost = 0.0f;
                        int validSamples = 0;

                        // Look at neighboring pixels in this historical frame
                        for (int dy = -radius; dy <= radius; dy += radius)
                        {
                            for (int dx = -radius; dx <= radius; dx += radius)
                            {
                                if (dx == 0 && dy == 0) continue;

                                float neighborDepth = SampleDepth(x + dx, y + dy, t, rollingWindow);
                                if (neighborDepth > 0.0f)
                                {
                                    // If neighbor is closer to current depth than the historical center pixel,
                                    // use it as evidence for stability
                                    float neighborDiff = XMath.Abs(neighborDepth - currentDepth);
                                    if (neighborDiff < depthDiff)
                                    {
                                        spatialBoost += 1.0f - (neighborDiff / depthDiff);
                                        validSamples++;
                                    }
                                }
                            }
                        }

                        // Apply spatial boost if we found valid neighbors
                        if (validSamples > 0)
                        {
                            spatialBoost /= validSamples;
                            // Scale boost by inverse of edge weight to preserve edges
                            frameWeight *= (1.0f + spatialBoost * (1.0f - edgeWeight));
                        }
                    }

                    // 6. Accumulate weighted contributions and squares for variance
                    weightedSum += frameWeight * frameDepth;
                    weightedSquareSum += frameWeight * frameDepth * frameDepth;
                    totalWeight += frameWeight;
                }

                // Calculate weighted average
                filteredDepth = weightedSum / totalWeight;

                // Guard against excessive deviation from current frame to reduce ghosting
                float maxDeviation = 0.02f * (1.0f - currentFrameConfidence);
                float deviation = XMath.Abs(filteredDepth - currentDepth);

                if (deviation > maxDeviation)
                {
                    // If deviation is too large, blend back toward current frame
                    float blendFactor = maxDeviation / deviation;
                    filteredDepth = currentDepth * (1.0f - blendFactor) + filteredDepth * blendFactor;
                }

                // Compute variance from the weighted square sum
                float variance = (weightedSquareSum / totalWeight) - (filteredDepth * filteredDepth);
                // If the temporal variance is too high, fallback toward the current frame
                if (variance > rollingWindow.varianceThreshold)
                {
                    float blendFactor = rollingWindow.varianceThreshold / variance;
                    blendFactor = XMath.Min(blendFactor, 1.0f);
                    filteredDepth = currentDepth * (1.0f - blendFactor) + filteredDepth * blendFactor;
                }
            }

            // Output the filtered depth
            output[index] = filteredDepth;
        }
    }
}
