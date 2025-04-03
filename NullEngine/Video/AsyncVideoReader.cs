using System;
using System.Diagnostics;
using System.Threading;
using OpenCvSharp;

namespace NullEngine.Video
{
    public class AsyncVideoReader : IFrameReader
    {
        private VideoCapture capture;
        private Thread frameReadThread;
        private bool isRunning;
        private double frameIntervalMs;
        private volatile bool isPaused = false;

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

        private bool singleFrameAdvance;
        private bool useRGBA;

        // We no longer use EndOfVideo to stop looping.
        public bool EndOfVideo { get; private set; } = false;

        // New field to track if the video has looped.
        private volatile bool hasLooped = false;
        public bool HasLooped => hasLooped;

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
                while (isRunning)
                {
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
                            // Reset to beginning and mark as looped.
                            capture.Set(VideoCaptureProperties.PosFrames, 0);
                            hasLooped = true;
                            frameRead = capture.Read(temp);
                            if (!frameRead)
                                continue;
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
                    }
                    frameReadyEvent.Set();
                }
            }
            else
            {
                var timer = Stopwatch.StartNew();
                long nextFrameTime = 0;

                while (isRunning)
                {
                    if (isPaused)
                    {
                        Thread.Sleep(50);
                        continue;
                    }

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
                                // Reset to beginning and mark as looped.
                                capture.Set(VideoCaptureProperties.PosFrames, 0);
                                hasLooped = true;
                                frameRead = capture.Read(temp);
                                if (!frameRead)
                                    continue;
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
                        Thread.Sleep((int)Math.Max(1, sleepTime));
                }
            }
        }

        public void PopFrame()
        {
            if (singleFrameAdvance)
            {
                frameAdvanceEvent.Set();
                frameReadyEvent.WaitOne();
            }
        }

        public nint GetCurrentFramePtr()
        {
            lock (bufferLock)
            {
                return frameMats[currentBufferIndex].Data;
            }
        }

        public void Play()
        {
            if (!singleFrameAdvance)
                isPaused = false;
        }

        public void Pause()
        {
            if (!singleFrameAdvance)
                isPaused = true;
        }

        public void Stop()
        {
            if (!singleFrameAdvance)
            {
                Pause();
                capture.Set(VideoCaptureProperties.PosFrames, 0);
            }
        }

        public double GetCurrentPosition()
        {
            return capture.Get(VideoCaptureProperties.PosMsec);
        }

        public void Seek(double posMsec)
        {
            if (!singleFrameAdvance)
            {
                lock (bufferLock)
                {
                    capture.Set(VideoCaptureProperties.PosMsec, posMsec);
                }
            }
        }

        public void Dispose()
        {
            isRunning = false;
            if (singleFrameAdvance)
                frameAdvanceEvent.Set();

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
