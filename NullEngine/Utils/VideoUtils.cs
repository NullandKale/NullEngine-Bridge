using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using OpenCvSharp;

namespace NullEngine.Utils
{
    public class AsyncVideoReader : IDisposable
    {
        private VideoCapture capture;
        private Thread frameReadThread;
        private bool isRunning;
        private double frameIntervalMs;

        // Double buffer components
        private Mat[] frameMats = new Mat[2];
        private int currentBufferIndex = 0;
        private object bufferLock = new object();

        public string VideoFile { get; }
        public int Width { get; }
        public int Height { get; }
        public double Fps { get; }

        public AsyncVideoReader(string videoFile)
        {
            VideoFile = videoFile;

            capture = new VideoCapture(videoFile, VideoCaptureAPIs.FFMPEG);
            if (!capture.IsOpened())
                throw new ArgumentException($"Could not open video file: {videoFile}");

            Width = capture.FrameWidth;
            Height = capture.FrameHeight;
            Fps = capture.Fps;
            frameIntervalMs = 1000.0 / Fps;

            // Initialize double buffers
            frameMats[0] = new Mat(Height, Width, MatType.CV_8UC3);
            frameMats[1] = new Mat(Height, Width, MatType.CV_8UC3);

            isRunning = true;
            frameReadThread = new Thread(FrameReadLoop);
            frameReadThread.Start();
        }

        private void FrameReadLoop()
        {
            var timer = Stopwatch.StartNew();
            long nextFrameTime = 0;

            while (isRunning)
            {
                try
                {
                    long currentTime = timer.ElapsedMilliseconds;

                    if (currentTime >= nextFrameTime)
                    {
                        int nextBufferIndex = 1 - currentBufferIndex;
                        Mat targetMat = frameMats[nextBufferIndex];

                        bool frameRead = capture.Read(targetMat);
                        if (!frameRead)
                        {
                            // Loop video when reaching end
                            capture.PosFrames = 0;
                            frameRead = capture.Read(targetMat);
                        }

                        if (frameRead)
                        {
                            lock (bufferLock)
                            {
                                currentBufferIndex = nextBufferIndex;
                            }
                            // Schedule next frame based on actual frame interval
                            nextFrameTime = currentTime + (long)frameIntervalMs;
                        }
                    }

                    // Calculate remaining time until next frame
                    long sleepTime = nextFrameTime - timer.ElapsedMilliseconds;
                    if (sleepTime > 0)
                    {
                        Thread.Sleep((int)Math.Max(1, sleepTime));
                    }
                }
                catch (ThreadAbortException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Frame read error: {ex.Message}");
                    isRunning = false;
                }
            }
        }

        public IntPtr GetCurrentFramePtr()
        {
            lock (bufferLock)
            {
                return frameMats[currentBufferIndex].Data;
            }
        }

        public void Dispose()
        {
            isRunning = false;

            if (frameReadThread != null && frameReadThread.IsAlive)
            {
                frameReadThread.Join(1000);
                if (frameReadThread.IsAlive)
                    frameReadThread.Abort();
            }

            capture?.Dispose();
            frameMats[0]?.Dispose();
            frameMats[1]?.Dispose();
        }
    }

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

        public Mat frameMat;

        public VideoReader(string videoFile)
        {
            this.videoFile = videoFile;

            capture = new VideoCapture(videoFile, VideoCaptureAPIs.FFMPEG);
            if (!capture.IsOpened())
                throw new ArgumentException($"Could not open video file: {videoFile}");

            Width = capture.FrameWidth;
            Height = capture.FrameHeight;
            Fps = capture.Fps;

            // Allocate & pin the int[] once. Each element is RGBA (4 bytes).
            frameBuffer = new int[Width * Height];
            pinnedHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
            pinnedPtr = pinnedHandle.AddrOfPinnedObject();

            frameMat = new Mat();
        }

        /// <summary>
        /// Reads the next frame from the video into the pinned int[] buffer.
        /// Returns true if a frame was read, false if there are no more frames.
        /// 
        /// You can access the frame data via the 'FrameBuffer' property as RGBA32 (0xAARRGGBB).
        /// </summary>
        public unsafe bool ReadFrame()
        {
            return capture.Read(frameMat);
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