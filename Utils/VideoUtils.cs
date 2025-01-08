using System;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace NullEngine.Utils
{

    public class VideoReader : IDisposable
    {
        private readonly VideoCapture capture;

        // One pinned buffer for the entire lifetime of the reader
        private readonly int[] frameBuffer;
        private GCHandle pinnedHandle;
        public IntPtr pinnedPtr;

        public string videoFile;
        public int Width;
        public int Height;
        public double Fps;

        public VideoReader(string videoFile)
        {
            this.videoFile = videoFile;

            capture = new VideoCapture(videoFile);
            if (!capture.IsOpened())
                throw new ArgumentException($"Could not open video file: {videoFile}");

            Width = capture.FrameWidth;
            Height = capture.FrameHeight;
            Fps = capture.Fps;

            // Allocate & pin the int[] once. Each element is RGBA (4 bytes).
            frameBuffer = new int[Width * Height];
            pinnedHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
            pinnedPtr = pinnedHandle.AddrOfPinnedObject();
        }

        /// <summary>
        /// Reads the next frame from the video into the pinned int[] buffer.
        /// Returns true if a frame was read, false if there are no more frames.
        /// 
        /// You can access the frame data via the 'FrameBuffer' property as RGBA32 (0xAARRGGBB).
        /// </summary>
        public unsafe bool ReadFrame()
        {
            using var mat = new Mat();
            bool success = capture.Read(mat);
            if (!success)
                return false;

            // Convert BGR -> RGBA in-place
            Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2RGBA);

            // Bulk copy from the Mat data (byte*) to our pinned int[] (byte*)
            IntPtr srcPtr = mat.Data;
            long totalBytes = (long)Width * Height * 4; // 4 bytes per pixel

            Buffer.MemoryCopy(
                source: (void*)srcPtr,
                destination: (void*)pinnedPtr,
                destinationSizeInBytes: totalBytes,
                sourceBytesToCopy: totalBytes
            );

            return true;
        }

        /// <summary>
        /// Gets the pinned RGBA32 buffer for the most recent frame read by ReadFrame().
        /// </summary>
        public int[] FrameBuffer => frameBuffer;

        public void Dispose()
        {
            pinnedHandle.Free();
            capture?.Dispose();
        }
    }

    public class VideoWriter : IDisposable
    {
        private readonly OpenCvSharp.VideoWriter writer;
        private readonly int width;
        private readonly int height;

        // One pinned buffer for the entire lifetime of the writer
        private readonly int[] frameBuffer;
        private GCHandle pinnedHandle;
        private IntPtr pinnedPtr;

        public VideoWriter(string outputFile, double fps, int width, int height)
        {
            this.width = width;
            this.height = height;

            writer = new OpenCvSharp.VideoWriter(
                outputFile,
                FourCC.MP4V,             // FourCC for MP4
                fps,
                new OpenCvSharp.Size(width, height)
            );

            if (!writer.IsOpened())
                throw new ArgumentException($"Could not create video file: {outputFile}");

            // Allocate & pin once for output
            frameBuffer = new int[width * height];
            pinnedHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
            pinnedPtr = pinnedHandle.AddrOfPinnedObject();
        }

        /// <summary>
        /// Writes a single frame (int[] RGBA32) into the output file.
        /// Internally uses a pinned buffer plus a single color convert call.
        /// </summary>
        public unsafe void WriteFrame(int[] rgbaData)
        {
            if (rgbaData == null || rgbaData.Length != width * height)
                throw new ArgumentException("Frame data must be RGBA32 with length == width*height.");

            // Copy the caller's RGBA data into our pinned buffer
            Buffer.BlockCopy(rgbaData, 0, frameBuffer, 0, width * height * 4);

            using var mat = Mat.FromPixelData(height, width, MatType.CV_8UC4, pinnedPtr);

            // Convert RGBA -> BGR in-place
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2BGR);

            // Write to the video
            writer.Write(mat);
        }

        public void Dispose()
        {
            pinnedHandle.Free();
            writer?.Dispose();
        }
    }

}