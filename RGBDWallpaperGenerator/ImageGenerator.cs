using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;

namespace RGBDWallpaperGenerator
{
    public static class ImageGenerator
    {
        // New flag to determine which API endpoint to use.
        private static bool usePath = false;
        private static readonly string Endpoint = usePath
            ? "http://127.0.0.1:5000/generate_file"
            : "http://127.0.0.1:5000/generate";

        // Image folders and prompt cache
        private static readonly string ImagesFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated_images");
        private static readonly string PromptCacheFile = Path.Combine(ImagesFolder, "prompt_cache.json");
        private static Dictionary<string, string> promptCache;

        // Voting folders
        private static readonly string UpvotedFolder = Path.Combine(ImagesFolder, "upvoted");
        private static readonly string DownvotedFolder = Path.Combine(ImagesFolder, "downvoted");
        private static readonly string UpvotedMeta = Path.Combine(UpvotedFolder, "metadata.jsonl");
        private static readonly string DownvotedMeta = Path.Combine(DownvotedFolder, "metadata.jsonl");

        // Prompt collections
        private static readonly ConcurrentBag<string> allPrompts = new ConcurrentBag<string>();
        private static readonly string promptsFilePath = Path.Combine(".", "Assets", "Prompts", "fancy_prompts.txt");
        private static readonly string newPromptsFilePath = Path.Combine(".", "Assets", "Prompts", "new_prompts.txt");
        private static readonly string basePromptsFilePath = Path.Combine(".", "Assets", "Prompts", "base_prompts.txt");

        // Thread-safe queue for work items
        private static readonly ConcurrentQueue<PromptItem> promptQueue = new ConcurrentQueue<PromptItem>();

        // Worker thread control
        private static Thread workerThread;
        private static volatile bool running = true;

        // Max prompt length
        private const int maxLength = 450;

        // Initialize folders, load cache and prompts, start worker
        static ImageGenerator()
        {
            Directory.CreateDirectory(ImagesFolder);
            Directory.CreateDirectory(UpvotedFolder);
            Directory.CreateDirectory(DownvotedFolder);

            promptCache = LoadPromptCache();

            // Pre-load allPrompts from both files
            if (File.Exists(promptsFilePath))
                foreach (var line in File.ReadAllLines(promptsFilePath))
                    if (!string.IsNullOrWhiteSpace(line))
                        allPrompts.Add(LimitPromptLength(line.Trim()));
            if (File.Exists(newPromptsFilePath))
                foreach (var line in File.ReadAllLines(newPromptsFilePath))
                    if (!string.IsNullOrWhiteSpace(line))
                        allPrompts.Add(LimitPromptLength(line.Trim()));

            workerThread = new Thread(WorkerLoop) { IsBackground = true };
            workerThread.Start();
        }

        // Upvote/downvote helper
        public static void Vote(string imagePath, bool upvote)
        {
            if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath))
                return;

            // Determine target and opposite vote folders/metadata
            var targetFolder = upvote ? UpvotedFolder : DownvotedFolder;
            var targetMeta = upvote ? UpvotedMeta : DownvotedMeta;
            var otherFolder = upvote ? DownvotedFolder : UpvotedFolder;
            var otherMeta = upvote ? DownvotedMeta : UpvotedMeta;
            var filename = Path.GetFileName(imagePath);
            var destTargetPath = Path.Combine(targetFolder, filename);
            var destOtherPath = Path.Combine(otherFolder, filename);

            // Lookup the prompt text
            var promptEntry = promptCache.FirstOrDefault(kv => kv.Value == imagePath);
            var promptText = promptEntry.Key ?? "";

            // Helper: remove any existing entry from the opposite metadata
            if (File.Exists(otherMeta))
            {
                var lines = File.ReadAllLines(otherMeta)
                                .Where(line => !line.Contains($"\"image\":\"{destOtherPath}\""))
                                .ToArray();
                File.WriteAllLines(otherMeta, lines);
                // Also delete the opposite‐folder copy if it exists
                if (File.Exists(destOtherPath))
                    File.Delete(destOtherPath);
            }

            // If the target metadata already contains this image, do nothing
            bool alreadyVoted = File.Exists(targetMeta)
                && File.ReadLines(targetMeta)
                       .Any(line => line.Contains($"\"image\":\"{destTargetPath}\""));

            if (alreadyVoted)
            {
                Console.WriteLine($"[Vote] Already {(upvote ? "up" : "down")}-voted: \"{promptText}\" → {filename}");
                return;
            }

            // Copy into the target folder and append metadata
            Directory.CreateDirectory(targetFolder);
            File.Copy(imagePath, destTargetPath, overwrite: true);

            var entry = JsonSerializer.Serialize(new { prompt = promptText, image = destTargetPath });
            File.AppendAllText(targetMeta, entry + Environment.NewLine);

            Console.WriteLine(upvote
                ? $"[Vote] Upvoted:   \"{promptText}\" → {filename}"
                : $"[Vote] Downvoted: \"{promptText}\" → {filename}");
        }


        // Enqueue a prompt for processing (with optional LLM enhancement)
        public static void EnqueuePrompt(string prompt, int width, int height, Action<string> onComplete, bool improve = false)
        {
            promptQueue.Enqueue(new PromptItem
            {
                Prompt = LimitPromptLength(prompt),
                Width = width,
                Height = height,
                Callback = onComplete,
                Improve = improve
            });
        }

        // Graceful shutdown
        public static void Stop()
        {
            running = false;
            workerThread.Join();
        }

        // Worker thread: improve prompt (if requested), then fetch/generate image
        private static void WorkerLoop()
        {
            while (running)
            {
                if (promptQueue.TryDequeue(out var item))
                {
                    string effectivePrompt = item.Prompt;

                    // 1) LLM enhancement on background thread
                    if (item.Improve)
                    {
                        var rnd = new Random();
                        var examples = allPrompts
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .OrderBy(_ => rnd.Next())
                            .Take(5)
                            .ToList();

                        var improved = OpenRouter.GenerateImprovedPrompt(effectivePrompt, examples)
                                                 .Trim();
                        improved = LimitPromptLength(improved);

                        File.AppendAllText(newPromptsFilePath, improved + Environment.NewLine);
                        allPrompts.Add(improved);
                        effectivePrompt = improved;
                    }

                    // 2) Generate or fetch image
                    var savedPath = GenerateOrFetchImage(effectivePrompt, item.Width, item.Height);

                    // 3) Invoke callback
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, true, true);
                    item.Callback?.Invoke(savedPath);
                }
                else
                {
                    Thread.Sleep(50);
                }
            }
        }

        // Core image‐generation or cache lookup
        private static string GenerateOrFetchImage(string prompt, int width, int height)
        {
            lock (promptCache)
            {
                if (promptCache.TryGetValue(prompt, out var existing) && File.Exists(existing))
                {
                    Console.WriteLine("Cache hit: " + existing);
                    return existing;
                }
            }

            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
                var payload = new { prompt, width, height };
                var content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                var response = client.PostAsync(Endpoint, content).Result;
                response.EnsureSuccessStatusCode();

                if (usePath)
                {
                    var doc = JsonDocument.Parse(response.Content.ReadAsStringAsync().Result);
                    var path = doc.RootElement.GetProperty("file_path").GetString();
                    Console.WriteLine("Server provided path: " + path);
                    return path;
                }
                else
                {
                    var data = response.Content.ReadAsByteArrayAsync().Result;
                    var filename = Path.Combine(ImagesFolder, "generated_" + Guid.NewGuid() + ".png");
                    File.WriteAllBytes(filename, data);
                    Console.WriteLine("Saved image: " + filename);

                    lock (promptCache)
                    {
                        promptCache[prompt] = filename;
                        SavePromptCache(promptCache);
                    }

                    return filename;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Error generating image: " + ex.Message);
                return null;
            }
        }

        // Prompt‐length limiter
        private static string LimitPromptLength(string s)
            => s?.Length > maxLength ? s.Substring(0, maxLength) : s;

        // Load cache from disk
        private static Dictionary<string, string> LoadPromptCache()
        {
            if (File.Exists(PromptCacheFile))
            {
                try
                {
                    var json = File.ReadAllText(PromptCacheFile);
                    return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                        ?? new Dictionary<string, string>();
                }
                catch
                {
                    Console.WriteLine("Failed to load prompt cache.");
                }
            }
            return new Dictionary<string, string>();
        }

        // Save cache to disk
        private static void SavePromptCache(Dictionary<string, string> cache)
        {
            try
            {
                var json = JsonSerializer.Serialize(cache, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(PromptCacheFile, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Failed to save prompt cache: " + ex.Message);
            }
        }

        // Internal work item
        private class PromptItem
        {
            public string Prompt = "";
            public bool Improve = false;
            public int Width = 0;
            public int Height = 0;
            public Action<string> Callback = null;
        }

        // Read the next line from the combined prompt files and enqueue
        public static void NextPrompt(Action<string> onComplete, int width = 1024, int height = 1024)
        {
            if (!allPrompts.TryTake(out var line))
            {
                Console.WriteLine("No prompts available.");
                return;
            }
            EnqueuePrompt(line, width, height, onComplete, improve: false);
        }

        // Pick five random base prompts, then enqueue with improvement
        public static void NewPrompt(Action<string> onComplete, int width = 1024, int height = 1024)
        {
            if (!File.Exists(basePromptsFilePath))
            {
                Console.WriteLine("Base prompts file not found: " + basePromptsFilePath);
                return;
            }

            var lines = File.ReadAllLines(basePromptsFilePath)
                            .Where(l => !string.IsNullOrWhiteSpace(l))
                            .Select(l => LimitPromptLength(l.Trim()))
                            .ToArray();

            if (lines.Length == 0)
            {
                Console.WriteLine("No valid base prompts found in: " + basePromptsFilePath);
                return;
            }

            var rnd = new Random();
            var pick = lines[rnd.Next(lines.Length)];

            EnqueuePrompt(pick, width, height, onComplete, improve: true);
            Console.WriteLine("NewPrompt enqueued with base prompt: \"" + pick + "\"");
        }

        /// <summary>
        /// Enqueue the given prompt for LLM‐based enhancement before image generation.
        /// </summary>
        /// <param name="prompt">The base prompt to improve.</param>
        /// <param name="onComplete">Callback receiving the generated image path.</param>
        /// <param name="width">Desired image width.</param>
        /// <param name="height">Desired image height.</param>
        public static void ImprovePrompt(string prompt,
                                         Action<string> onComplete,
                                         int width = 1024,
                                         int height = 1024)
        {
            // Simply enqueue with the 'Improve' flag set
            EnqueuePrompt(
                LimitPromptLength(prompt.Trim().Replace('\n', ' ')),
                width,
                height,
                onComplete,
                improve: true
            );
        }

    }
}
