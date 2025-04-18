using ILGPU.Runtime;
using ILGPU;

namespace GPU
{
    public static class FocusKernels
    {
        public static void AnalyzeDepthForAutoFocus(
            Index1D index,
            ArrayView1D<float, Stride1D.Dense> depthMap,
            ArrayView1D<int, Stride1D.Dense> histogram,
            ArrayView1D<float, Stride1D.Dense> depthMinMax,
            int width,
            int height)
        {

        }

        public static void RemapDepthForAutoFocus(
            Index1D index,
            ArrayView1D<float, Stride1D.Dense> depthMap,
            ArrayView1D<float, Stride1D.Dense> depthMinMax,
            float focusDepth,
            float autoFocusStrength)
        {

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
        private Accelerator _device;

        // GPU buffers
        public MemoryBuffer1D<int, Stride1D.Dense> DepthHistogram;
        public MemoryBuffer1D<float, Stride1D.Dense> DepthMinMax;

        private const int HistogramBins = 256;

        // We'll replicate the same constants from the GPU side for local maxima
        private const int MinPeakHeight = 100;
        private const int MinPeakDistance = 10;

        public DepthAutoFocusAndStats(Accelerator device)
        {
            _device = device;

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
            MemoryBuffer1D<float, Stride1D.Dense> depthBuffer,
            int width,
            int height,
            float lastFocusDepth,
            float focusSmoothing,
            float autoFocusStrength)
        {
            int totalPixels = (int)depthBuffer.Length;

            // (4) Build histogram on GPU
            int[] histogramCPU = new int[HistogramBins];
            DepthHistogram.CopyFromCPU(histogramCPU);
            


            DepthHistogram.CopyToCPU(histogramCPU);


            float rawFocusDepth = FindBestFocusDepthCPU(histogramCPU);

            // (6) Smooth with old focus
            float newFocusDepth = lastFocusDepth * focusSmoothing +
                                  rawFocusDepth * (1f - focusSmoothing);

            // (7) Remap depth on GPU

            _device.Synchronize();

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
            List<(int pos, int height)> peaks = new List<(int pos, int height)>(20);

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
                        if (Math.Abs(i - peaks[p].pos) < MinPeakDistance)
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
            int peaksToUse = Math.Min(2, peaks.Count);
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
