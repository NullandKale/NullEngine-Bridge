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

        // Allocate 20 frames, each of size 'frameSize' (for example, totalPixels).
        public DepthRollingWindow(Accelerator device, int frameSize)
        {
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
            return new dDepthRollingWindow(
                CurrentIndex,
                Frame0, Frame1, Frame2, Frame3, Frame4,
                Frame5, Frame6, Frame7, Frame8, Frame9,
                Frame10, Frame11, Frame12, Frame13, Frame14,
                Frame15, Frame16, Frame17, Frame18, Frame19);
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
            int index,
            MemoryBuffer1D<float, Stride1D.Dense> frame0,
            MemoryBuffer1D<float, Stride1D.Dense> frame1,
            MemoryBuffer1D<float, Stride1D.Dense> frame2,
            MemoryBuffer1D<float, Stride1D.Dense> frame3,
            MemoryBuffer1D<float, Stride1D.Dense> frame4,
            MemoryBuffer1D<float, Stride1D.Dense> frame5,
            MemoryBuffer1D<float, Stride1D.Dense> frame6,
            MemoryBuffer1D<float, Stride1D.Dense> frame7,
            MemoryBuffer1D<float, Stride1D.Dense> frame8,
            MemoryBuffer1D<float, Stride1D.Dense> frame9,
            MemoryBuffer1D<float, Stride1D.Dense> frame10,
            MemoryBuffer1D<float, Stride1D.Dense> frame11,
            MemoryBuffer1D<float, Stride1D.Dense> frame12,
            MemoryBuffer1D<float, Stride1D.Dense> frame13,
            MemoryBuffer1D<float, Stride1D.Dense> frame14,
            MemoryBuffer1D<float, Stride1D.Dense> frame15,
            MemoryBuffer1D<float, Stride1D.Dense> frame16,
            MemoryBuffer1D<float, Stride1D.Dense> frame17,
            MemoryBuffer1D<float, Stride1D.Dense> frame18,
            MemoryBuffer1D<float, Stride1D.Dense> frame19)
        {
            this.index = index;
            Frame0 = frame0.View;
            Frame1 = frame1.View;
            Frame2 = frame2.View;
            Frame3 = frame3.View;
            Frame4 = frame4.View;
            Frame5 = frame5.View;
            Frame6 = frame6.View;
            Frame7 = frame7.View;
            Frame8 = frame8.View;
            Frame9 = frame9.View;
            Frame10 = frame10.View;
            Frame11 = frame11.View;
            Frame12 = frame12.View;
            Frame13 = frame13.View;
            Frame14 = frame14.View;
            Frame15 = frame15.View;
            Frame16 = frame16.View;
            Frame17 = frame17.View;
            Frame18 = frame18.View;
            Frame19 = frame19.View;
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
        /// <summary>
        /// Filtering kernel that averages the 20 depth frames for each pixel using the time-ordered indexer.
        /// </summary>
        public static void FilterDepthRollingWindow(
            Index1D index,
            dDepthRollingWindow rollingWindow,
            ArrayView<float> output)
        {
            // Most recent frame value.
            float v0 = rollingWindow[0][index];
            if (v0 == 0.0f)
            {
                output[index] = 0.0f;
                return;
            }

            // Parameters to control the weighting:
            // tau: controls the age decay (lower tau means older frames count less).
            // delta: if the difference from v0 is below delta, treat it as negligible.
            // sigma: controls the exponential decay beyond delta.
            const float tau = 3.0f;
            const float delta = 3.0f;
            const float sigma = 7.5f;

            float weightedSum = 0.0f;
            float weightTotal = 0.0f;
            float sumSq = 0.0f; // For computing weighted variance

            // Loop over all 20 frames.
            for (int i = 0; i < 20; i++)
            {
                float vi = rollingWindow[i][index];
                if (vi == 0.0f)
                    continue;

                // Older frames (higher i) are downweighted exponentially.
                float weightAge = XMath.Exp(-(float)i / tau);

                // Compute the difference weight.
                float diff = XMath.Abs(vi - v0);
                float weightDiff = (diff < delta) ? 1.0f : XMath.Exp(-(diff - delta) / sigma);

                float weight = weightAge * weightDiff;
                weightedSum += weight * vi;
                weightTotal += weight;
                sumSq += weight * vi * vi;
            }

            // If no valid frames contributed, fall back to the most recent value.
            if (weightTotal == 0.0f)
            {
                output[index] = v0;
                return;
            }

            // Compute weighted average and variance.
            float avg = weightedSum / weightTotal;
            float variance = sumSq / weightTotal - avg * avg;

            // Define a variance threshold.
            // When variance is low (stable scene), we use the temporal average.
            // When variance is high (scene change), we lean more on the most recent value.
            const float varThreshold = 1.0f;
            float blendFactor = XMath.Min(variance / varThreshold, 1.0f);

            // Blend: if blendFactor is 0 then output is the average;
            // if blendFactor is 1 then output is the most recent value.
            float filtered = (1.0f - blendFactor) * avg + blendFactor * v0;
            output[index] = filtered;
        }




    }
}
