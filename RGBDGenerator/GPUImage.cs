using ILGPU;
using ILGPU.Algorithms;
using ILGPU.Runtime;
using OpenTK.Core;
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace GPU
{
    public class GPUImage : IDisposable
    {
        public int width;
        public int height;

        public bool dirty = false;
        public bool cpu_dirty = false;

        public int[] data;

        public MemoryBuffer1D<int, Stride1D.Dense>? gpuData;

        public GPUImage(int width, int height)
        {
            this.width = width;
            this.height = height;

            data = new int[width * height];
        }

        public GPUImage(int width, int height, byte[] data)
        {
            this.width = width;
            this.height = height;

            this.data = new int[width * height];
            Buffer.BlockCopy(data, 0, this.data, 0, data.Length);
        }

        public GPUImage(int width, int height, int[] data)
        {
            this.width = width;
            this.height = height;

            this.data = new int[width * height];
            Buffer.BlockCopy(data, 0, this.data, 0, data.Length * sizeof(int));
        }

        public GPUImage(int width, int height, IntPtr data)
        {
            this.width = width;
            this.height = height;
            this.data = new int[width * height];
            Marshal.Copy(data, this.data, 0, this.data.Length);
        }

        public GPUImage(float[,] floatData)
        {
            int width = floatData.GetLength(0);
            int height = floatData.GetLength(1);
            this.width = width;
            this.height = height;
            data = new int[width * height];

            // Find the maximum and minimum values in the floatData for normalization
            float max = float.MinValue;
            float min = float.MaxValue;
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    max = Math.Max(max, floatData[x, y]);
                    min = Math.Min(min, floatData[x, y]);
                }
            }

            // Normalization factor
            float range = max - min;

            // Convert the floatData to int[] in RGBA format
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Normalize the value between 0 and 255
                    float normalizedValue = ((floatData[x, y] - min) / range) * 255;
                    int intValue = (int)normalizedValue;
                    data[y * width + x] = (255 << 24) | (intValue << 16) | (intValue << 8) | intValue; // RGBA format
                }
            }
        }

        public GPUImage(Bitmap b)
        {
            this.width = b.Width;
            this.height = b.Height;
            byte[] data_bytes = Utils.BitmapToBytes(b);
            data = new int[width * height];
            Buffer.BlockCopy(data_bytes, 0, data, 0, data_bytes.Length);
        }

        public static bool TryLoadFromUrl(string url, out GPUImage image)
        {
            try
            {
                using (HttpClient httpClient = new HttpClient())
                {
                    // Synchronously wait for the task to complete
                    byte[] data = httpClient.GetByteArrayAsync(url).Result;
                    using (MemoryStream ms = new MemoryStream(data))
                    {
                        using (Bitmap bmp = new Bitmap(ms))
                        {
                            image = new GPUImage(bmp);
                            return true;
                        }
                    }
                }
            }
            catch (Exception)
            {
                image = null;
                return false;
            }
        }

        public static bool TryLoad(string file, out GPUImage image)
        {
            string ext = Path.GetExtension(file);

            if (File.Exists(file) && ext.Length > 2)
            {
                Bitmap bmp;
                try
                {
                    bmp = new Bitmap(file);
                }
                catch (Exception)
                {
                    image = null;
                    return false;
                }
                image = new GPUImage(bmp);
                bmp.Dispose();

                return true;
            }

            image = null;
            return false;
        }

        public dImage toDevice(Accelerator gpu)
        {
            if (gpuData == null || gpuData.Extent != data.Length)
            {
                if (data != null && data.Length > 0)
                {
                    gpuData = gpu.Allocate1D(data);
                }
                else
                {
                    gpuData = gpu.Allocate1D<int>(width * height);
                }
            }

            if (cpu_dirty)
            {
                gpuData.CopyFromCPU(data);
                cpu_dirty = false;
            }

            dirty = true;
            return new dImage(width, height, gpuData);
        }

        public ref int[] toCPU()
        {
            if (gpuData != null)
            {
                if (data == null || data.Length != gpuData.Length)
                {
                    data = new int[gpuData.Length];
                    dirty = true;
                }

                if (dirty)
                {
                    gpuData.CopyToCPU(data);
                    dirty = false;
                }
            }

            return ref data;
        }

        public void fromCPU_UNSAFE(IntPtr data)
        {
            Marshal.Copy(data, this.data, 0, this.data.Length);
            cpu_dirty = true;
        }

        public Bitmap GetBitmap()
        {
            return Utils.BitmapFromBytes(toCPU(), width, height);
        }

        public void Dispose()
        {
            gpuData?.Dispose();
        }
    }

    public struct dImage
    {
        public int width;
        public int height;

        public ArrayView1D<int, Stride1D.Dense> data;

        public dImage(int width, int height, MemoryBuffer1D<int, Stride1D.Dense> data)
        {
            this.width = width;
            this.height = height;
            this.data = data;
        }

        public RGBA32 GetColorAt(int x, int y)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return new RGBA32();
            }

            int index = (y * width + x);
            return new RGBA32(data[index]);
        }

        public void SetColorAt(int x, int y, int color)
        {
            if (x < 0 || x >= width || y < 0 || y >= height)
            {
                return;
            }

            int index = (y * width + x);
            data[index] = color;
        }

        public void SetColor(float x, float y, Vec3 color)
        {
            SetColorAt((int)(x * width), (int)(y * height), new RGBA32(color));
        }

        public void SetColorAt(int x, int y, RGBA32 color)
        {
            SetColorAt(x, y, color.ToInt());
        }

        public RGBA32 GetColorAt(float x, float y)
        {
            float clammpedX = XMath.Clamp(x, 0, 1);
            float clammpedY = XMath.Clamp(y, 0, 1);
            float x_idx = XMath.Floor(clammpedX * (float)width);
            float y_idx = XMath.Floor(clammpedY * (float)height);

            int index = (int)(y_idx * (float)width + x_idx);

            if (index < 0 || index >= data.Length)
            {
                return new RGBA32(1, 0, 1);
            }

            return new RGBA32(data[index]);
        }

        public Vec3 GetPixel(float x, float y)
        {
            int x_idx = (int)(x * width);
            int y_idx = (int)(y * height);

            return new Vec3(GetColorAt(x_idx, y_idx));
        }
    }
}