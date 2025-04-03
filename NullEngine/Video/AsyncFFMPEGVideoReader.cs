using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using OpenCvSharp;
using Microsoft.Win32.SafeHandles;

namespace NullEngine.Video
{
    public static class WindowsJob
    {
        private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern nint CreateJobObject(nint lpJobAttributes, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetInformationJobObject(nint hJob, JOBOBJECTINFOCLASS infoClass,
            nint lpJobObjectInfo, uint cbJobObjectInfoLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AssignProcessToJobObject(nint hJob, nint hProcess);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool TerminateProcess(nint hProcess, uint uExitCode);

        private enum JOBOBJECTINFOCLASS
        {
            BasicLimitInformation = 2,
            ExtendedLimitInformation = 9,
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_BASIC_LIMIT_INFORMATION
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public nuint MinimumWorkingSetSize;
            public nuint MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public nint Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct IO_COUNTERS
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
        {
            public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
            public IO_COUNTERS IoInfo;
            public nuint ProcessMemoryLimit;
            public nuint JobMemoryLimit;
            public nuint PeakProcessMemoryUsed;
            public nuint PeakJobMemoryUsed;
        }

        private static readonly nint jobHandle;

        static WindowsJob()
        {
            jobHandle = CreateJobObject(nint.Zero, null);

            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int length = Marshal.SizeOf(info);
            nint infoPtr = Marshal.AllocHGlobal(length);
            Marshal.StructureToPtr(info, infoPtr, false);

            SetInformationJobObject(jobHandle, JOBOBJECTINFOCLASS.ExtendedLimitInformation,
                infoPtr, (uint)length);

            Marshal.FreeHGlobal(infoPtr);
        }

        public static void AddProcess(Process process)
        {
            if (process != null && !process.HasExited)
            {
                AssignProcessToJobObject(jobHandle, process.Handle);
            }
        }
    }
}

namespace NullEngine.Video
{
    public class AsyncFfmpegVideoReader : IFrameReader
    {
        private Thread frameReadThread;
        private bool isRunning;
        private bool isPaused;
        private volatile bool hasLooped;

        private readonly bool singleFrameAdvance;
        private AutoResetEvent frameAdvanceEvent;
        private AutoResetEvent frameReadyEvent;

        private readonly object bufferLock = new object();
        private readonly Mat[] frameMats = new Mat[2];
        private int currentBufferIndex = 0;

        private Process ffmpegProcess;
        private Stream ffmpegStdOut;
        private byte[] readBuffer;

        private Process audioProcess;

        public string VideoFile { get; }
        public int Width { get; }
        public int Height { get; }
        public double Fps { get; }

        private readonly int bytesPerPixel;
        private readonly int frameBytes;

        private double frameIntervalMs;
        private Stopwatch timer;
        private double nextFrameTime;

        public bool HasLooped => hasLooped;

        public AsyncFfmpegVideoReader(
            string videoFile,
            bool singleFrameAdvance = false,
            bool useRGBA = false,
            bool playAudio = true)
        {
            VideoFile = videoFile;

            using (var tmpCap = new VideoCapture(videoFile, VideoCaptureAPIs.FFMPEG))
            {
                if (!tmpCap.IsOpened())
                    throw new ArgumentException($"Could not open video file: {videoFile}");
                Width = tmpCap.FrameWidth;
                Height = tmpCap.FrameHeight;
                Fps = tmpCap.Fps;
            }

            this.singleFrameAdvance = singleFrameAdvance;
            // Remove interactive–disabling flags from ffplay.
            string ffmpegPixFmt = useRGBA ? "bgra" : "bgr24";
            MatType matType = useRGBA ? MatType.CV_8UC4 : MatType.CV_8UC3;
            bytesPerPixel = useRGBA ? 4 : 3;

            frameMats[0] = new Mat(Height, Width, matType);
            frameMats[1] = new Mat(Height, Width, matType);

            if (singleFrameAdvance)
            {
                frameAdvanceEvent = new AutoResetEvent(false);
                frameReadyEvent = new AutoResetEvent(false);
            }

            ffmpegProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "ffmpeg.exe",
                    Arguments = $"-hwaccel cuda -i \"{videoFile}\" -f rawvideo -pix_fmt {ffmpegPixFmt} pipe:1",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };
            ffmpegProcess.ErrorDataReceived += (sender, e) => { /* Optional logging */ };
            ffmpegProcess.Start();
            WindowsJob.AddProcess(ffmpegProcess);
            ffmpegProcess.BeginErrorReadLine();
            ffmpegStdOut = ffmpegProcess.StandardOutput.BaseStream;

            frameBytes = Width * Height * bytesPerPixel;
            readBuffer = new byte[frameBytes];

            // Launch ffplay without autoexit or nodisp so that a window is created and interactive input can be processed.
            if (!singleFrameAdvance && playAudio)
            {
                LaunchAudio(videoFile);
            }

            isRunning = true;
            frameReadThread = new Thread(FrameReadLoop) { IsBackground = true };
            frameReadThread.Start();

            if (!singleFrameAdvance)
            {
                frameIntervalMs = 1000.0 / Fps;
                timer = Stopwatch.StartNew();
                nextFrameTime = 0;
            }
        }

        private void LaunchAudio(string videoFile)
        {
            var audioPsi = new ProcessStartInfo
            {
                FileName = "ffplay.exe",
                Arguments = $"-loglevel error -autoexit -nodisp -i \"{videoFile}\"",
                UseShellExecute = false,
                RedirectStandardOutput = false,
                RedirectStandardError = false,
                RedirectStandardInput = false,
                CreateNoWindow = false
            };

            audioProcess = new Process { StartInfo = audioPsi };
            audioProcess.Start();
            WindowsJob.AddProcess(audioProcess);
        }

        private void FrameReadLoop()
        {
            if (singleFrameAdvance)
            {
                while (isRunning)
                {
                    frameAdvanceEvent.WaitOne();
                    if (!isRunning) break;
                    if (!ReadOneFrame(out bool looped))
                    {
                        LoopOrBreak();
                    }
                    else if (looped)
                    {
                        hasLooped = true;
                    }
                    frameReadyEvent.Set();
                }
            }
            else
            {
                if (timer == null)
                    timer = Stopwatch.StartNew();
                double nextFrameTimestamp = timer.Elapsed.TotalMilliseconds;
                double frameDuration = 1000.0 / Fps;
                while (isRunning)
                {
                    if (isPaused)
                    {
                        Thread.Sleep(10);
                        continue;
                    }
                    double currentMs = timer.Elapsed.TotalMilliseconds;
                    if (currentMs < nextFrameTimestamp)
                    {
                        double remaining = nextFrameTimestamp - currentMs;
                        if (remaining > 2.0)
                        {
                            Thread.Sleep((int)(remaining - 1));
                        }
                        else
                        {
                            Thread.SpinWait(50);
                        }
                        continue;
                    }
                    if (!ReadOneFrame(out bool looped))
                    {
                        LoopOrBreak();
                    }
                    else if (looped)
                    {
                        hasLooped = true;
                    }
                    nextFrameTimestamp += frameDuration;
                }
            }
        }

        private bool ReadOneFrame(out bool looped)
        {
            looped = false;
            int totalRead = 0;
            while (totalRead < frameBytes)
            {
                int n = ffmpegStdOut.Read(readBuffer, totalRead, frameBytes - totalRead);
                if (n <= 0)
                {
                    return false;
                }
                totalRead += n;
            }
            int nextBufferIndex = 1 - currentBufferIndex;
            Mat targetMat = frameMats[nextBufferIndex];
            Marshal.Copy(readBuffer, 0, targetMat.Data, frameBytes);
            lock (bufferLock)
            {
                currentBufferIndex = nextBufferIndex;
            }
            return totalRead == frameBytes;
        }

        private void LoopOrBreak()
        {
            try
            {
                ffmpegProcess?.Kill();
            }
            catch
            {
            }
            ffmpegProcess?.Dispose();
            hasLooped = true;
            var info = ffmpegProcess?.StartInfo;
            if (info == null) return;
            ffmpegProcess = new Process { StartInfo = info };
            ffmpegProcess.ErrorDataReceived += (sender, e) => { };
            ffmpegProcess.Start();
            WindowsJob.AddProcess(ffmpegProcess);
            ffmpegProcess.BeginErrorReadLine();
            ffmpegStdOut = ffmpegProcess.StandardOutput.BaseStream;
        }

        public void PopFrame()
        {
            if (!singleFrameAdvance)
                return;
            frameAdvanceEvent.Set();
            frameReadyEvent.WaitOne();
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

        /// <summary>
        /// Instead of trying to gracefully shut down, this method forcefully kills the ffplay process using taskkill.
        /// </summary>
        public void Stop()
        {
            if (!singleFrameAdvance)
            {
                Pause();
                if (audioProcess != null && !audioProcess.HasExited)
                {
                    try
                    {
                        Console.WriteLine("Stop(): Executing taskkill to force ffplay termination.");
                        ForceKillAudio();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("Stop(): Exception during taskkill: " + ex);
                    }
                }
            }
        }

        private void ForceKillAudio()
        {
            // Launch a new process to run taskkill with /F /T /PID.
            Process killer = new Process();
            killer.StartInfo.FileName = "taskkill";
            killer.StartInfo.Arguments = $"/F /T /PID {audioProcess.Id}";
            killer.StartInfo.CreateNoWindow = true;
            killer.StartInfo.UseShellExecute = false;
            killer.Start();
            killer.WaitForExit();
            audioProcess.WaitForExit();
        }

        public void Dispose()
        {
            isRunning = false;
            if (singleFrameAdvance)
            {
                frameAdvanceEvent.Set();
            }
            if (frameReadThread != null && frameReadThread.IsAlive)
            {
                frameReadThread.Join(1000);
                if (frameReadThread.IsAlive)
                {
#pragma warning disable SYSLIB0003
                    frameReadThread.Abort();
#pragma warning restore SYSLIB0003
                }
            }

            if (audioProcess != null)
            {
                try
                {
                    if (!audioProcess.HasExited)
                    {
                        Console.WriteLine("Dispose(): Executing taskkill to force ffplay termination.");
                        ForceKillAudio();
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Dispose(): Exception during taskkill: " + ex);
                }
                audioProcess?.Dispose();
            }

            try
            {
                ffmpegStdOut?.Close();
                if (ffmpegProcess != null && !ffmpegProcess.HasExited)
                {
                    ffmpegProcess.Kill();
                    ffmpegProcess.WaitForExit();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Dispose(): Exception on ffmpeg shutdown: " + ex);
            }
            ffmpegProcess?.Dispose();

            frameMats[0]?.Dispose();
            frameMats[1]?.Dispose();
            frameAdvanceEvent?.Dispose();
            frameReadyEvent?.Dispose();
        }
    }
}
