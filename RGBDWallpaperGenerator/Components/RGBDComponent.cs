using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NullEngine.Renderer.Components;
using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Shaders;
using NullEngine.Renderer.Textures;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace RGBDWallpaperGenerator.Components
{
    public class RGBDComponent : IComponent
    {
        public string Filename;
        private double scale = 1;
        public Texture texture = null;
        public Shader RGBDShader;

        private string pendingFilename;

        // ------ Auto‑prompting via Y Key ------
        private bool autoGenerating = false;
        private bool isPromptProcessing = false;
        // -------------------------------------

        // ------ Playback (P), Next/Prev (←/→) ------
        private bool playbackMode = false;
        private List<string> playbackFiles = null;
        private int playbackIndex = 0;
        private double playbackTimer = 0.0;
        private double playbackDelay = 10.0;
        // -----------------------------------------

        // ------ Auto‑Theme Mode via H Key ------
        private bool autoThemeMode = false;
        private bool isThemePromptProcessing = false;
        private string autoThemeValue = "";
        // ---------------------------------------

        // ------ Render‑mode (M key) ------
        private int mode = 2;   // 0 = colour, 1 = debug‑depth, 2 = composite
        // ---------------------------------

        public RGBDComponent()
        {
            Filename = "";
            texture = null;
            Program.window.FileDrop += FileDrop;

            RGBDShader = new Shader(
                // vertex
                @"
                #version 330 core
                layout (location = 0) in vec3 position;
                layout (location = 1) in vec3 normal;
                layout (location = 2) in vec2 texCoords;
                out vec2 fragTexCoords;
                out vec2 screenUV;
                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;
                uniform sampler2D textureSampler;
                uniform int   mode;
                uniform float depthScale = 1.0;
                uniform float depthBias  = 0.0;
                uniform float depthPower = 1.0;
                vec2 getDepthTC(vec2 tc,int m){
                    return (m==2)?((tc.x<0.5)?vec2(tc.x+0.5,tc.y):tc)
                                 :vec2(tc.x*0.5+0.5,tc.y);
                }
                vec2 getColorTC(vec2 tc,int m){
                    if(m==0) return vec2(tc.x*0.5,tc.y);
                    if(m==1) return vec2(tc.x*0.5+0.5,tc.y);
                    return tc;
                }
                void main(){
                    float rawDepth = texture(textureSampler, getDepthTC(texCoords,mode)).r;
                    float d = (1.0-pow(rawDepth,depthPower))*depthScale - depthBias;
                    vec3 displaced = position + normal * d;
                    gl_Position   = projection * view * model * vec4(displaced,1.0);
                    fragTexCoords = getColorTC(texCoords,mode);
                    screenUV      = texCoords;
                }
                ",
                // fragment
                @"
                #version 450 core
                in vec2 fragTexCoords;
                in vec2 screenUV;
                out vec4 FragColor;
                uniform sampler2D textureSampler;
                uniform int   mode;
                uniform float depthCutoffFS;
                uniform vec2  depthTexSize;
                vec2 depthTC(vec2 tc,int m){
                    return (m==2)?((tc.x<0.5)?vec2(tc.x+0.5,tc.y):tc)
                                 :vec2(tc.x*0.5+0.5,tc.y);
                }
                void main(){
                    vec2 dTC = depthTC(screenUV,mode);
                    vec2 px  = 1.0 / depthTexSize;
                    float center = texture(textureSampler,dTC).r;
                    float sum = 0.0;
                    for(int j=-1; j<=1; ++j)
                        for(int i=-1; i<=1; ++i)
                            sum += abs(texture(textureSampler,dTC + vec2(i,j)*px).r - center);
                    if(sum/9.0 > depthCutoffFS) discard;
                    FragColor = texture(textureSampler,fragTexCoords);
                }
                ");
            RGBDShader.SetUniform("depthCutoffFS", 0.05f);
        }

        private void FileDrop(FileDropEventArgs e)
        {
            if (e.FileNames.Length > 0 && File.Exists(e.FileNames[0]))
            {
                pendingFilename = e.FileNames[0];
                Console.WriteLine($"[FileDrop] queued: {pendingFilename}");
            }
        }

        private (int width, int height) GetImageDimensions()
        {
            var sz = Program.window.Size;
            return sz.X >= sz.Y
                ? ((int)(1280 * scale), (int)(720 * scale))
                : ((int)(720 * scale), (int)(1280 * scale));
        }

        public void HandleMouseInput(BaseMesh mesh, MouseState ms, Vector2 delta, bool isPressed)
        {
            // no-op
        }

        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState ks, float deltaTime)
        {
            // ─── Space: generate one new prompt ───
            if (ks.IsKeyPressed(Keys.Space))
            {
                var (w, h) = GetImageDimensions();
                Console.WriteLine("[Input] Space pressed → NextPrompt");
                ImageGenerator.NewPrompt(path => pendingFilename = path, w, h);
            }

            // ─── Y: toggle auto‑prompt ───
            if (ks.IsKeyPressed(Keys.Y))
            {
                autoGenerating = !autoGenerating;
                Console.WriteLine($"[Input] Auto prompt {(autoGenerating ? "enabled" : "disabled")}");
            }

            // ─── P: toggle playback of all generated images ───
            if (ks.IsKeyPressed(Keys.P))
            {
                playbackMode = !playbackMode;
                Console.WriteLine(playbackMode ? "[Input] Playback START" : "[Input] Playback PAUSE");
                if (playbackMode)
                {
                    // load full generated_images folder
                    EnsurePlaybackFilesLoaded();
                    Console.WriteLine($"[Playback] {playbackFiles.Count} generated files loaded.");
                    if (playbackFiles.Count > 0)
                    {
                        playbackIndex = 0;
                        pendingFilename = playbackFiles[playbackIndex];
                        Console.WriteLine($"[Playback] Starting at index {playbackIndex}: {pendingFilename}");
                    }
                    playbackTimer = 0.0;
                }
            }

            // ─── [: start playback of upvoted images ───
            if (ks.IsKeyPressed(Keys.LeftBracket))
            {
                playbackMode = true;
                var upvotedDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated_images", "upvoted");
                playbackFiles = Directory.Exists(upvotedDir)
                    ? Directory.GetFiles(upvotedDir, "*.png")
                               .OrderByDescending(f => File.GetCreationTime(f))
                               .ToList()
                    : new List<string>();
                Console.WriteLine($"[Playback] Upvoted playback START: {playbackFiles.Count} files loaded.");
                if (playbackFiles.Count > 0)
                {
                    playbackIndex = 0;
                    pendingFilename = playbackFiles[playbackIndex];
                    Console.WriteLine($"[Playback] Starting at index {playbackIndex}: {pendingFilename}");
                }
                playbackTimer = 0.0;
            }

            // ─── ]: start playback of downvoted images ───
            if (ks.IsKeyPressed(Keys.RightBracket))
            {
                playbackMode = true;
                var downvotedDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated_images", "downvoted");
                playbackFiles = Directory.Exists(downvotedDir)
                    ? Directory.GetFiles(downvotedDir, "*.png")
                               .OrderByDescending(f => File.GetCreationTime(f))
                               .ToList()
                    : new List<string>();
                Console.WriteLine($"[Playback] Downvoted playback START: {playbackFiles.Count} files loaded.");
                if (playbackFiles.Count > 0)
                {
                    playbackIndex = 0;
                    pendingFilename = playbackFiles[playbackIndex];
                    Console.WriteLine($"[Playback] Starting at index {playbackIndex}: {pendingFilename}");
                }
                playbackTimer = 0.0;
            }

            // ─── O: upvote current ───
            if (ks.IsKeyPressed(Keys.O))
            {
                if (!string.IsNullOrEmpty(Filename))
                {
                    ImageGenerator.Vote(Filename, upvote: true);

                    // advance only if in playback mode
                    if (playbackMode && playbackFiles.Count > 0)
                    {
                        playbackIndex = (playbackIndex + 1) % playbackFiles.Count;
                        pendingFilename = playbackFiles[playbackIndex];
                        Console.WriteLine($"[Playback] After upvote → index {playbackIndex}: {pendingFilename}");
                    }
                }
            }

            // ─── L: downvote current ───
            if (ks.IsKeyPressed(Keys.L))
            {
                if (!string.IsNullOrEmpty(Filename))
                {
                    ImageGenerator.Vote(Filename, upvote: false);

                    // advance only if in playback mode
                    if (playbackMode && playbackFiles.Count > 0)
                    {
                        playbackIndex = (playbackIndex + 1) % playbackFiles.Count;
                        pendingFilename = playbackFiles[playbackIndex];
                        Console.WriteLine($"[Playback] After downvote → index {playbackIndex}: {pendingFilename}");
                    }
                }
            }

            // ─── I: next image in the current playbackFiles ───
            if (ks.IsKeyPressed(Keys.I) && playbackMode && playbackFiles.Count > 0)
            {
                playbackIndex = (playbackIndex + 1) % playbackFiles.Count;
                pendingFilename = playbackFiles[playbackIndex];
                Console.WriteLine($"[Playback] Next → index {playbackIndex}: {pendingFilename}");
            }

            // ─── U: previous image in the current playbackFiles ───
            if (ks.IsKeyPressed(Keys.U) && playbackMode && playbackFiles.Count > 0)
            {
                playbackIndex = (playbackIndex - 1 + playbackFiles.Count) % playbackFiles.Count;
                pendingFilename = playbackFiles[playbackIndex];
                Console.WriteLine($"[Playback] Prev ← index {playbackIndex}: {pendingFilename}");
            }

            // ─── H: toggle auto‑theme ───
            if (ks.IsKeyPressed(Keys.H))
            {
                autoThemeMode = !autoThemeMode;
                Console.WriteLine(autoThemeMode
                    ? $"[Input] Auto theme START: \"{(autoThemeValue = Program.window.ClipboardString)}\""
                    : "[Input] Auto theme STOP");
            }

            // ─── M: cycle render mode ───
            if (ks.IsKeyPressed(Keys.M))
            {
                mode = (mode + 1) % 3;
                Console.WriteLine($"[Input] Render mode → {mode}");
            }

            // ─── Ctrl+V: clipboard as prompt ───
            bool ctrl = ks.IsKeyDown(Keys.LeftControl) || ks.IsKeyDown(Keys.RightControl);
            if (ctrl && ks.IsKeyPressed(Keys.V))
            {
                var clip = Program.window.ClipboardString;
                if (!string.IsNullOrWhiteSpace(clip))
                {
                    var (w, h) = GetImageDimensions();
                    Console.WriteLine($"[Input] Clipboard prompt: \"{clip}\"");
                    ImageGenerator.ImprovePrompt(clip, path => pendingFilename = path, w, h);
                }
            }
        }


        private void EnsurePlaybackFilesLoaded()
        {
            var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "generated_images");
            playbackFiles = Directory.Exists(dir)
                ? Directory.GetFiles(dir, "*.png")
                           .OrderByDescending(f => File.GetCreationTime(f))
                           .ToList()
                : new List<string>();
        }

        private void LoadTexture(BaseMesh mesh)
        {
            // 1) If there's a freshly queued filename, attempt to load it—otherwise keep the old texture alive.
            if (!string.IsNullOrEmpty(pendingFilename))
            {
                var candidate = pendingFilename;
                pendingFilename = null;

                if (File.Exists(candidate))
                {
                    try
                    {
                        TextureManager.LoadTexture("RGBD", candidate, false);
                        var newTex = TextureManager.GetTexture("RGBD");
                        if (newTex == null || newTex.width <= 0 || newTex.height <= 0)
                            throw new Exception("Invalid texture data");

                        // Success: swap in the new texture and update the stored Filename
                        texture = newTex;
                        Filename = candidate;
                        Console.WriteLine($"[Texture] Loaded: {Filename} ({texture.width}×{texture.height})");

                        // Re‑compute scale & shader uniform
                        float ar = (mode == 2)
                            ? (float)texture.width / texture.height
                            : (texture.width / 2f) / texture.height;

                        mesh.Transform.Scale = mode == 2
                            ? new Vector3(ar * 0.5f, 0.5f, 0.5f)
                            : new Vector3(ar, 1, 1);

                        RGBDShader.SetUniform("depthTexSize",
                            new Vector2(texture.width * 0.5f, texture.height));
                    }
                    catch (Exception ex)
                    {
                        // On failure, log and leave 'texture' pointing at whatever was showing before
                        Console.WriteLine($"[Texture] Failed to load '{candidate}': {ex.Message}");
                    }
                }
            }

            // 2) If we still don't have any texture (e.g. at startup), load the existing Filename once.
            if (texture == null && File.Exists(Filename))
            {
                try
                {
                    TextureManager.LoadTexture("RGBD", Filename, false);
                    var initialTex = TextureManager.GetTexture("RGBD");
                    if (initialTex == null || initialTex.width <= 0 || initialTex.height <= 0)
                        throw new Exception("Invalid texture data");

                    texture = initialTex;
                    Console.WriteLine($"[Texture] Loaded initial: {Filename} ({texture.width}×{texture.height})");

                    float ar = (mode == 2)
                        ? (float)texture.width / texture.height
                        : (texture.width / 2f) / texture.height;

                    mesh.Transform.Scale = mode == 2
                        ? new Vector3(ar * 0.5f, 0.5f, 0.5f)
                        : new Vector3(ar, 1, 1);

                    RGBDShader.SetUniform("depthTexSize",
                        new Vector2(texture.width * 0.5f, texture.height));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Texture] Failed to load initial '{Filename}': {ex.Message}");
                }
            }
        }

        public void Update(BaseMesh mesh, float deltaTime)
        {
            LoadTexture(mesh);

            // Only replace the mesh's texture/shader when we have a valid `texture`
            if (texture != null)
            {
                mesh.Texture = texture;
                mesh.shader = RGBDShader;
                RGBDShader.SetUniform("mode", mode);

                float ar = mode == 2
                    ? (float)texture.width / texture.height
                    : (texture.width / 2f) / texture.height;

                mesh.Transform.Scale = mode == 2
                    ? new Vector3(ar * 0.5f, 0.5f, 0.5f)
                    : new Vector3(ar, 1, 1);

                RGBDShader.SetUniform("depthTexSize",
                    new Vector2(texture.width * 0.5f, texture.height));

                Program.window.SetOverrideRGBD(texture);
            }

            // --------- Auto‑prompting mode ---------
            if (autoGenerating && !isPromptProcessing)
            {
                isPromptProcessing = true;
                var (w, h) = GetImageDimensions();
                ImageGenerator.NewPrompt(path =>
                {
                    pendingFilename = path;
                    isPromptProcessing = false;
                }, w, h);
            }

            // --------- Auto‑theme mode ---------
            if (autoThemeMode && !isThemePromptProcessing)
            {
                isThemePromptProcessing = true;
                if (!string.IsNullOrWhiteSpace(autoThemeValue))
                {
                    var (w, h) = GetImageDimensions();
                    ImageGenerator.ImprovePrompt(autoThemeValue, path =>
                    {
                        pendingFilename = path;
                        isThemePromptProcessing = false;
                    }, w, h);
                }
                else
                {
                    isThemePromptProcessing = false;
                }
            }

            // --------- Playback mode ---------
            if (playbackMode)
            {
                playbackTimer += deltaTime;
                if (playbackTimer >= playbackDelay)
                {
                    if (playbackFiles.Count > 0)
                    {
                        pendingFilename = playbackFiles[playbackIndex];
                        Console.WriteLine($"[Playback] Auto‑advance to index {playbackIndex}: {pendingFilename}");
                        playbackIndex = (playbackIndex + 1) % playbackFiles.Count;
                    }
                    playbackTimer = 0.0;
                }
            }
        }


        public object Clone()
        {
            return new RGBDComponent { Filename = this.Filename };
        }
    }
}
