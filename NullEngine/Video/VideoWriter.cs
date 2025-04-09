using System;
using System.IO;

namespace NullEngine.Video
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
}
