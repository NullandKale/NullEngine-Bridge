using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace NullEngine.Video
{
    // Implementor that uses ffmpeg (NVENC) for video encoding with raw RGBA input.
    internal class FfmpegVideoWriter : IVideoWriter
    {
        private readonly int width;
        private readonly int height;
        private readonly double fps;
        private readonly Process ffmpegProcess;
        private readonly Stream ffmpegStdIn;

        public FfmpegVideoWriter(string outputFile, double fps, int width, int height, string audioInputFile, string ffmpegPath)
        {
            this.width = width;
            this.height = height;
            this.fps = fps;

            // Build ffmpeg command arguments.
            // The input is raw RGBA data and the filter converts it to yuv420p.
            string arguments;
            if (string.IsNullOrEmpty(audioInputFile))
            {
                arguments = $"-y -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - " +
                            $"-vf format=yuv420p " +
                            $"-c:v hevc_nvenc -preset medium -rc constqp -qp 22 -movflags faststart \"{outputFile}\"";
            }
            else
            {
                arguments = $"-y -f rawvideo -pix_fmt rgba -s {width}x{height} -r {fps} -i - " +
                            $"-i \"{audioInputFile}\" -map 0:v:0 -map 1:a:0 " +
                            $"-vf format=yuv420p " +
                            $"-c:v hevc_nvenc -preset medium -rc constqp -qp 22 -c:a copy -shortest -movflags faststart \"{outputFile}\"";
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

            // Start asynchronous reading of the error stream.
            ffmpegProcess.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    Console.WriteLine(e.Data);
                }
            };
            ffmpegProcess.BeginErrorReadLine();
        }

        // WriteFrame now accepts RGBA32 data and writes it directly to ffmpeg without a block copy.
        public void WriteFrame(int[] rgbaData)
        {
            if (rgbaData == null || rgbaData.Length != width * height)
                throw new ArgumentException("Frame data must be RGBA32 with length == width*height.");

            // Reinterpret the int[] as a span of bytes without copying.
            ffmpegStdIn.Write(MemoryMarshal.AsBytes(rgbaData.AsSpan()));
        }

        public void Dispose()
        {
            try
            {
                ffmpegStdIn.Close();
                if (!ffmpegProcess.WaitForExit(5000))
                {
                    ffmpegProcess.Kill();
                }
            }
            catch { /* Ignore exceptions on disposal. */ }
            ffmpegProcess?.Dispose();
        }
    }
}
