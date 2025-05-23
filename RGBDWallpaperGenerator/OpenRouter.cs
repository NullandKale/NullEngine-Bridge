using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace RGBDWallpaperGenerator
{
    // -------------------------------------------------------------------------
    //  RandomPromptEnhancer (unchanged from v3)
    // -------------------------------------------------------------------------
    public static class RandomPromptEnhancer
    {
        private static readonly Random random = new Random();
        private const int MaxLength = 450;

        private static readonly string[] TimeSpecifiers =
            { "morning","noon","afternoon","dawn","sunrise","dusk",
              "sunset","evening","midnight","late‑night" };

        private static readonly string[] PrimaryColors =
            { "red","blue","green","yellow","purple","orange",
              "pink","cyan","magenta","lime" };

        private static readonly string[] WeatherConditions =
            { "sunny","rainy","foggy","stormy","snowy","windy",
              "overcast","humid","icy","cloudy" };

        private static readonly string[] Moods =
            { "vibrant","mysterious","serene","energetic","melancholic",
              "joyful","somber","whimsical","intense","dreamy" };

        private static readonly string[] CameraAngles =
            { "low‑angle","bird's‑eye view","worm's‑eye view","close‑up",
              "wide‑angle","high‑angle","overhead","perspective","panoramic",
              "side view" };

        private static readonly string[] Lighting =
            { "cinematic lighting","soft rim light","volumetric rays",
              "golden‑hour glow","dramatic chiaroscuro" };

        private static readonly string[] Lenses =
            { "35 mm DSLR","anamorphic lens","fisheye lens",
              "tilt‑shift shot","macro shot" };

        private static readonly string[] PostFx =
            { "Kodak Porta 400","high‑contrast","bokeh",
              "unreal engine render","Octane render" };

        private const float TimeChance = 0.55f; // was 0.85
        private const float PrimaryColorChance = 0.33f; // was 0.50
        private const float WeatherChance = 0.29f; // was 0.45
        private const float MoodChance = 0.59f; // was 0.90
        private const float CameraAngleChance = 0.20f; // was 0.30
        private const float LightingChance = 0.13f; // was 0.20
        private const float LensChance = 0.10f; // was 0.15
        private const float PostFxChance = 0.07f; // was 0.10

        public static string EnhancePrompt(string basePrompt)
        {
            if (string.IsNullOrWhiteSpace(basePrompt)) return basePrompt;

            var picks = new List<string>();
            var result = basePrompt.Trim();

            MaybeAdd(result, picks, TimeSpecifiers, TimeChance);
            MaybeAdd(result, picks, PrimaryColors, PrimaryColorChance);
            MaybeAdd(result, picks, WeatherConditions, WeatherChance);
            MaybeAdd(result, picks, Moods, MoodChance);
            MaybeAdd(result, picks, CameraAngles, CameraAngleChance);
            MaybeAdd(result, picks, Lighting, LightingChance);
            MaybeAdd(result, picks, Lenses, LensChance);
            MaybeAdd(result, picks, PostFx, PostFxChance);

            Shuffle(picks);
            if (picks.Count > 0) result += ", " + string.Join(" ", picks);

            if (result.Length > MaxLength)
            {
                result = result[..MaxLength];
            }

            return result;
        }

        private static void MaybeAdd(string basePrompt, List<string> acc,
                                     string[] pool, float chance)
        {
            if (random.NextDouble() >= chance) return;
            var pick = PickUnique(pool, acc, basePrompt);
            if (pick != null) acc.Add(pick);
        }

        private static string PickUnique(string[] pool, ICollection<string> used, string text)
        {
            for (int i = 0; i < 10; i++)
            {
                var c = pool[random.Next(pool.Length)];
                if (!used.Contains(c) &&
                    text.IndexOf(c, StringComparison.OrdinalIgnoreCase) < 0)
                    return c;
            }
            return null;
        }

        private static void Shuffle<T>(IList<T> list)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = random.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
        }
    }

    // -------------------------------------------------------------------------
    //  OpenRouter  (negative‑prompt removed)
    // -------------------------------------------------------------------------
    public static class OpenRouter
    {
        private const string Endpoint = "https://openrouter.ai/api/v1/chat/completions";
        private static readonly string ApiKey =
            Environment.GetEnvironmentVariable("OPENROUTER_API_KEY") ?? "";

        public static string GenerateImprovedPrompt(string basePrompt,
                                                    List<string> examples)
        {
            basePrompt = RandomPromptEnhancer.EnhancePrompt(basePrompt);
            var joinedExamples = string.Join("; ", examples.Where(e => !string.IsNullOrWhiteSpace(e)));

            var sys = "You are an image‑prompt formatter. "
                     + "Return ONLY a single line containing the improved prompt. "
                     + "No JSON, no markdown.";
            var user = $"BASE: \"{basePrompt}\"\nEXAMPLES: \"{joinedExamples}\"";

            Console.WriteLine(new string('═', 60));
            Console.WriteLine($"BASE: \"{basePrompt}\"");

            var req = new
            {
                model = "openai/gpt-4o-mini-2024-07-18",
                temperature = 1.2,
                messages = new object[]
                {
                    new { role = "system", content = sys },
                    new { role = "user",   content = user }
                }
            };

            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Authorization", $"Bearer {ApiKey}");

                var res = client.PostAsync(
                    Endpoint,
                    new StringContent(JsonSerializer.Serialize(req),
                                      Encoding.UTF8, "application/json")).Result;

                res.EnsureSuccessStatusCode();
                var raw = res.Content.ReadAsStringAsync().Result;

                var outer = JsonDocument.Parse(raw);
                var inner = outer.RootElement.GetProperty("choices")[0]
                              .GetProperty("message").GetProperty("content").GetString();

                var positive = string.IsNullOrWhiteSpace(inner) ? basePrompt : inner.Trim();
                if (positive.Length > 450) positive = positive[..450];
                Console.WriteLine($"positive: \"{positive}\"");

                return positive;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[OpenRouter ERROR] " + ex.Message);
                return basePrompt;   // graceful fallback
            }
        }
    }
}
