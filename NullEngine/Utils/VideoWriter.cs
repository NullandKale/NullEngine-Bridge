using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using OpenCvSharp;

namespace NullEngine.Utils
{
    // 1. The interface that declares the API for writing video frames.
    public interface IVideoWriter : IDisposable
    {
        void WriteFrame(int[] rgbaData);
    }

    // 2. The public facade that picks the implementor automatically.
    public class VideoWriter : IVideoWriter
    {
        private readonly IVideoWriter writer;

        /// <summary>
        /// Creates a new VideoWriter. If ffmpeg.exe is found (in PATH or at "./ffmpeg/ffmpeg.exe"),
        /// uses ffmpeg with NVENC (and optional audio from <paramref name="audioInputFile"/>);
        /// otherwise falls back to OpenCvSharp.
        /// </summary>
        /// <param name="outputFile">Output video file path.</param>
        /// <param name="fps">Frames per second.</param>
        /// <param name="width">Frame width.</param>
        /// <param name="height">Frame height.</param>
        /// <param name="audioInputFile">Optional video file to extract audio from.</param>
        public VideoWriter(string outputFile, double fps, int width, int height, string audioInputFile = null)
        {
            string ffmpegPath = FindFfmpegPath();
            if (!string.IsNullOrEmpty(ffmpegPath))
            {
                writer = new FfmpegVideoWriter(outputFile, fps, width, height, audioInputFile, ffmpegPath);
            }
            else
            {
                writer = new OpenCvVideoWriter(outputFile, fps, width, height);
            }
        }

        public void WriteFrame(int[] rgbaData)
        {
            writer.WriteFrame(rgbaData);
        }

        public void Dispose()
        {
            writer.Dispose();
        }

        /// <summary>
        /// Searches for ffmpeg.exe in the local folder or in the system PATH.
        /// </summary>
        private static string FindFfmpegPath()
        {
            // Check local folder "./ffmpeg/ffmpeg.exe"
            string localPath = Path.Combine(".", "ffmpeg", "ffmpeg.exe");
            if (File.Exists(localPath))
                return Path.GetFullPath(localPath);

            // Check the system PATH
            string pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (var path in pathEnv.Split(Path.PathSeparator))
                {
                    try
                    {
                        string fullPath = Path.Combine(path, "ffmpeg.exe");
                        if (File.Exists(fullPath))
                            return fullPath;
                    }
                    catch { }
                }
            }
            return null;
        }
    }

    // 3a. Implementor that uses ffmpeg (NVENC) for video encoding.
    internal class FfmpegVideoWriter : IVideoWriter
    {
        private readonly int width;
        private readonly int height;
        private readonly double fps;
        private readonly Process ffmpegProcess;
        private readonly Stream ffmpegStdIn;
        private readonly byte[] ffmpegFrameBuffer; // holds BGR24 frame data

        // A pinned buffer for RGBA conversion.
        private readonly int[] frameBuffer;
        private readonly GCHandle pinnedHandle;
        private readonly IntPtr pinnedPtr;

        public FfmpegVideoWriter(string outputFile, double fps, int width, int height, string audioInputFile, string ffmpegPath)
        {
            this.width = width;
            this.height = height;
            this.fps = fps;

            // Allocate and pin the RGBA buffer.
            frameBuffer = new int[width * height];
            pinnedHandle = GCHandle.Alloc(frameBuffer, GCHandleType.Pinned);
            pinnedPtr = pinnedHandle.AddrOfPinnedObject();

            // Allocate buffer for BGR24 frames (3 bytes per pixel).
            ffmpegFrameBuffer = new byte[width * height * 3];

            // Build ffmpeg command arguments.
            string arguments;
            if (string.IsNullOrEmpty(audioInputFile))
            {
                arguments = $"-y -f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fps} -i - " +
                            $"-c:v hevc_nvenc -preset slow -rc constqp -qp 22 \"{outputFile}\"";
            }
            else
            {
                arguments = $"-y -f rawvideo -pix_fmt bgr24 -s {width}x{height} -r {fps} -i - " +
                            $"-i \"{audioInputFile}\" -map 0:v:0 -map 1:a:0 " +
                            $"-c:v hevc_nvenc -preset slow -rc constqp -qp 22 -c:a copy -shortest \"{outputFile}\"";
            }

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = arguments,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            ffmpegProcess = Process.Start(psi);
            ffmpegStdIn = ffmpegProcess.StandardInput.BaseStream;

            // Start asynchronous reading of the error stream to prevent blocking.
            ffmpegProcess.ErrorDataReceived += (sender, e) =>
            {
                // Optionally log or ignore error output.
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Debug.WriteLine(e.Data);
                }
            };
            ffmpegProcess.BeginErrorReadLine();
        }

        public unsafe void WriteFrame(int[] rgbaData)
        {
            if (rgbaData == null || rgbaData.Length != width * height)
                throw new ArgumentException("Frame data must be RGBA32 with length == width*height.");

            // Copy the caller's RGBA data into our pinned buffer.
            Buffer.BlockCopy(rgbaData, 0, frameBuffer, 0, width * height * 4);

            using var mat = Mat.FromPixelData(height, width, MatType.CV_8UC4, pinnedPtr);
            // Convert RGBA -> BGR in-place.
            Cv2.CvtColor(mat, mat, ColorConversionCodes.RGBA2BGR);

            // Copy the BGR data into our byte buffer.
            Marshal.Copy(mat.Data, ffmpegFrameBuffer, 0, ffmpegFrameBuffer.Length);
            ffmpegStdIn.Write(ffmpegFrameBuffer, 0, ffmpegFrameBuffer.Length);
        }

        public void Dispose()
        {
            try
            {
                ffmpegStdIn.Close();
                // Wait for the process to exit, but use a timeout to avoid hanging.
                if (!ffmpegProcess.WaitForExit(5000))
                {
                    ffmpegProcess.Kill();
                }
            }
            catch { /* Ignore exceptions on disposal. */ }
            ffmpegProcess?.Dispose();

            if (pinnedHandle.IsAllocated)
                pinnedHandle.Free();
        }
    }

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
