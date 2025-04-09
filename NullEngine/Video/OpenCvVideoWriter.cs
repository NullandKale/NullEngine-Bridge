using System;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace NullEngine.Video
{
    // 3b. Implementor that uses OpenCvSharp for video encoding.
    internal class OpenCvVideoWriter : IVideoWriter
    {
        private readonly int width;
        private readonly int height;
        private readonly OpenCvSharp.VideoWriter writer;

        // A pinned buffer for RGBA conversion.
        private readonly int[] frameBuffer;
        private readonly GCHandle pinnedHandle;
        private readonly IntPtr pinnedPtr;

        public OpenCvVideoWriter(string outputFile, double fps, int width, int height)
        {
            this.width = width;
            this.height = height;

            writer = new OpenCvSharp.VideoWriter(
                outputFile,
                FourCC.MP4V,
                fps,
                new OpenCvSharp.Size(width, height)
            );
            if (!writer.IsOpened())
                throw new ArgumentException($"Could not create video file: {outputFile}");

            frameBuffer = new int[width * height];
            pinnedHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
            pinnedPtr = pinnedHandle.AddrOfPinnedObject();
        }

        public unsafe void WriteFrame(int[] rgbaData)
        {
            if (rgbaData == null || rgbaData.Length != width * height)
                throw new ArgumentException("Frame data must be RGBA32 with length == width*height.");

            Buffer.BlockCopy(rgbaData, 0, frameBuffer, 0, width * height * 4);

            using var mat = Mat.FromPixelData(height, width, MatType.CV_8UC4, pinnedPtr);
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2BGR);
            writer.Write(mat);
        }

        public void Dispose()
        {
            writer?.Dispose();
            if (pinnedHandle.IsAllocated)
                pinnedHandle.Free();
        }
    }
}
