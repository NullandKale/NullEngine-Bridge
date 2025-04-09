using System;
using System.Diagnostics;

namespace NullEngine.Video
{
    public class FfmpegAudioPlayer : IDisposable
    {
        private Process audioProcess;

        public FfmpegAudioPlayer(string videoFile)
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

        public void ForceKill()
        {
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
            try
            {
                if (audioProcess != null && !audioProcess.HasExited)
                {
                    ForceKill();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("FfmpegAudioPlayer Dispose(): Exception: " + ex);
            }
            audioProcess?.Dispose();
        }
    }
}
