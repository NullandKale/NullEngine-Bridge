using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace NullEngine
{
    public static class Log
    {
        private static BlockingCollection<string> _logQueue;
        private static Task _processingTask;
        private static CancellationTokenSource _cts;
        private static string _logFilePath;
        private static volatile bool _initialized = false;

        private static readonly object _initLock = new object();

        /// <summary>
        /// Initializes the logger with a specified file path. 
        /// This must be called before using any logging methods.
        /// </summary>
        /// <param name="logFilePath">The file to write logs to.</param>
        public static void Initialize(string folderPath)
        {
            lock (_initLock)
            {
                if (_initialized) return;

                if (string.IsNullOrWhiteSpace(folderPath))
                    throw new ArgumentException("folderPath cannot be null or empty.");

                // Ensure the directory exists
                folderPath = Path.GetFullPath(folderPath);
                if (!Directory.Exists(folderPath))
                {
                    Directory.CreateDirectory(folderPath);
                }

                // Generate a timestamped log file name
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                _logFilePath = Path.Combine(folderPath, $"log_{timestamp}.txt");

                _cts = new CancellationTokenSource();
                _logQueue = new BlockingCollection<string>(new ConcurrentQueue<string>());

                // Start the background log processing task
                _processingTask = Task.Run(() => ProcessLogQueue(_cts.Token));

                _initialized = true;
            }
        }

        /// <summary>
        /// Shuts down the logger, ensuring all queued log messages are written.
        /// Call this before application exit.
        /// </summary>
        public static void Shutdown()
        {
            if (!_initialized) return;

            _logQueue.CompleteAdding();
            _cts.Cancel(); // Signal cancellation to speed up if blocked

            try
            {
                _processingTask.Wait();
            }
            catch (AggregateException ex) when (ex.InnerExceptions.Count == 1 && ex.InnerException is OperationCanceledException)
            {
                // Expected if cancelled.
            }
            catch (OperationCanceledException)
            {
                // Expected if cancelled.
            }
            finally
            {
                _cts.Dispose();
                _logQueue.Dispose();
                _initialized = false;
            }
        }

        /// <summary>
        /// Logs a debug message.
        /// </summary>
        public static void Debug(string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            LogInternal(LogLevel.DEBUG, message, memberName, filePath, lineNumber);
        }

        /// <summary>
        /// Logs an informational message.
        /// </summary>
        public static void Info(string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            LogInternal(LogLevel.INFO, message, memberName, filePath, lineNumber);
        }

        /// <summary>
        /// Logs a warning message.
        /// </summary>
        public static void Warn(string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            LogInternal(LogLevel.WARN, message, memberName, filePath, lineNumber);
        }

        /// <summary>
        /// Logs an error message.
        /// </summary>
        public static void Error(string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            LogInternal(LogLevel.ERROR, message, memberName, filePath, lineNumber);
        }

        /// <summary>
        /// Logs a fatal error message (severe).
        /// </summary>
        public static void Fatal(string message,
            [CallerMemberName] string memberName = "",
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0)
        {
            LogInternal(LogLevel.FATAL, message, memberName, filePath, lineNumber);
        }

        private static void LogInternal(LogLevel level, string message, string memberName, string filePath, int lineNumber)
        {
            if (!_initialized)
            {
                // Fallback: If not initialized, just write to Console
                Console.WriteLine("[NOT INITIALIZED] " + FormatLogLine(level, message, memberName, filePath, lineNumber));
                return;
            }

            // Queue the message
            try
            {
                _logQueue.Add(FormatLogLine(level, message, memberName, filePath, lineNumber));
            }
            catch (InvalidOperationException)
            {
                // Thrown if adding to a completed collection - ignore if shutting down
            }
        }

        private static string FormatLogLine(LogLevel level, string message, string memberName, string filePath, int lineNumber)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            string fileName = Path.GetFileName(filePath);
            return $"{timestamp} [{level}] ({fileName}:{lineNumber} {memberName}) {message}";
        }

        private static void ProcessLogQueue(CancellationToken token)
        {
            // Open file stream with append mode and buffered stream
            using (var fs = new FileStream(_logFilePath, FileMode.Append, FileAccess.Write, FileShare.Read, 4096, FileOptions.WriteThrough))
            using (var sw = new StreamWriter(fs) { AutoFlush = true })
            {
                while (!_logQueue.IsCompleted && !token.IsCancellationRequested)
                {
                    try
                    {
                        // Try to take a log line
                        if (_logQueue.TryTake(out string logLine, Timeout.Infinite, token))
                        {
                            sw.WriteLine(logLine);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Exit gracefully on cancellation
                        break;
                    }
                    catch (Exception ex)
                    {
                        // If an error occurs writing to the log, write an error to console and continue
                        Console.Error.WriteLine($"[LogError] Unable to write log line: {ex.Message}");
                    }
                }

                // Process any remaining items after CompleteAdding() was called
                while (_logQueue.TryTake(out var remainingLine))
                {
                    sw.WriteLine(remainingLine);
                }
            }
        }

        private enum LogLevel
        {
            DEBUG,
            INFO,
            WARN,
            ERROR,
            FATAL
        }
    }
}
