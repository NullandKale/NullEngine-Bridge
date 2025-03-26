using System;
using System.Diagnostics;
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

        // Double buffer components.
        private Mat[] frameMats = new Mat[2];
        private int currentBufferIndex = 0;
        private object bufferLock = new object();

        // AutoResetEvents for single-frame advancement.
        private AutoResetEvent frameAdvanceEvent;
        private AutoResetEvent frameReadyEvent;

        public string VideoFile { get; }
        public int Width { get; }
        public int Height { get; }
        public double Fps { get; }

        // When true, the reader only advances when PopFrame is called.
        private bool singleFrameAdvance;
        // When true, output frames are converted to RGBA.
        private bool useRGBA;

        // Indicates that the video has reached its end.
        public bool EndOfVideo { get; private set; } = false;

        public AsyncVideoReader(string videoFile, bool singleFrameAdvance = false, bool useRGBA = false)
        {
            this.singleFrameAdvance = singleFrameAdvance;
            this.useRGBA = useRGBA;
            VideoFile = videoFile;

            capture = new VideoCapture(videoFile, VideoCaptureAPIs.FFMPEG);
            if (!capture.IsOpened())
                throw new ArgumentException($"Could not open video file: {videoFile}");

            Width = capture.FrameWidth;
            Height = capture.FrameHeight;
            Fps = capture.Fps;
            frameIntervalMs = 1000.0 / Fps;

            // Allocate double buffers with the desired Mat type.
            MatType matType = useRGBA ? MatType.CV_8UC4 : MatType.CV_8UC3;
            frameMats[0] = new Mat(Height, Width, matType);
            frameMats[1] = new Mat(Height, Width, matType);

            if (singleFrameAdvance)
            {
                frameAdvanceEvent = new AutoResetEvent(false);
                frameReadyEvent = new AutoResetEvent(false);
            }

            isRunning = true;
            frameReadThread = new Thread(FrameReadLoop);
            frameReadThread.Start();
        }

        private void FrameReadLoop()
        {
            if (singleFrameAdvance)
            {
                // Single-frame mode: wait for a signal to advance.
                while (isRunning && !EndOfVideo)
                {
                    // Wait until PopFrame signals to advance.
                    frameAdvanceEvent.WaitOne();
                    if (!isRunning)
                        break;

                    int nextBufferIndex = 1 - currentBufferIndex;
                    Mat targetMat = frameMats[nextBufferIndex];

                    using (Mat temp = new Mat())
                    {
                        bool frameRead = capture.Read(temp);
                        if (!frameRead)
                        {
                            // No more frames. Set flag and signal ready to avoid blocking PopFrame.
                            EndOfVideo = true;
                            frameReadyEvent.Set();
                            break;
                        }

                        if (useRGBA)
                        {
                            // Convert BGR to RGBA.
                            Cv2.CvtColor(temp, targetMat, ColorConversionCodes.RGB2RGBA);
                        }
                        else
                        {
                            temp.CopyTo(targetMat);
                        }

                        lock (bufferLock)
                        {
                            currentBufferIndex = nextBufferIndex;
                        }
                    }
                    // Signal that the new frame is ready.
                    frameReadyEvent.Set();
                }
            }
            else
            {
                // Automatic mode: advance frames at a fixed interval.
                var timer = Stopwatch.StartNew();
                long nextFrameTime = 0;

                while (isRunning && !EndOfVideo)
                {
                    long currentTime = timer.ElapsedMilliseconds;
                    if (currentTime >= nextFrameTime)
                    {
                        int nextBufferIndex = 1 - currentBufferIndex;
                        Mat targetMat = frameMats[nextBufferIndex];

                        using (Mat temp = new Mat())
                        {
                            bool frameRead = capture.Read(temp);
                            if (!frameRead)
                            {
                                // End-of-video reached.
                                EndOfVideo = true;
                                break;
                            }

                            if (useRGBA)
                            {
                                Cv2.CvtColor(temp, targetMat, ColorConversionCodes.RGB2RGBA);
                            }
                            else
                            {
                                temp.CopyTo(targetMat);
                            }

                            lock (bufferLock)
                            {
                                currentBufferIndex = nextBufferIndex;
                            }
                            nextFrameTime = currentTime + (long)frameIntervalMs;
                        }
                    }

                    long sleepTime = nextFrameTime - timer.ElapsedMilliseconds;
                    if (sleepTime > 0)
                    {
                        Thread.Sleep((int)Math.Max(1, sleepTime));
                    }
                }
            }
        }

        /// <summary>
        /// In single-frame mode, signals the reader to advance to the next frame and blocks until the frame is loaded.
        /// In automatic mode, this method is a no-op.
        /// </summary>
        public void PopFrame()
        {
            if (singleFrameAdvance && !EndOfVideo)
            {
                frameAdvanceEvent.Set();
                // Block until the frame is loaded or we reach the end.
                frameReadyEvent.WaitOne();
            }
        }

        /// <summary>
        /// Returns a pointer to the data of the current frame.
        /// </summary>
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
            if (singleFrameAdvance)
            {
                // Signal to unblock the waiting thread.
                frameAdvanceEvent.Set();
            }

            if (frameReadThread != null && frameReadThread.IsAlive)
            {
                frameReadThread.Join(1000);
                if (frameReadThread.IsAlive)
                    frameReadThread.Abort();
            }

            capture?.Dispose();
            frameMats[0]?.Dispose();
            frameMats[1]?.Dispose();
            frameAdvanceEvent?.Dispose();
            frameReadyEvent?.Dispose();
        }
    }
}
