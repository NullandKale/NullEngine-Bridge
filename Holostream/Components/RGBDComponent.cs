// RGBDComponent.cs
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using Holostream.Network;
using NullEngine.Renderer.Components;
using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Shaders;
using NullEngine.Renderer.Textures;
using NullEngine.Video;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace Holostream.Components
{
    public class RGBDComponent : IComponent, IDisposable
    {
        public string URL = "http://localhost:1337/";
        private readonly int width = 1280, height = 720, quality = 65;

        private readonly Shader RGBDShader;
        private readonly StreamClient camClient;
        private Texture cameraTexture;

        private AsyncCameraReader cameraReader;
        private AsyncVideoReader videoReader;

        private static readonly string[] VideoExts = { ".mp4", ".mov", ".avi", ".mkv", ".webm" };

        public RGBDComponent()
        {
            RGBDShader = new Shader(
                """
                #version 330 core
                layout (location = 0) in vec3 position;
                layout (location = 1) in vec3 normal;
                layout (location = 2) in vec2 texCoords;
                out vec2 fragTexCoords;
                uniform mat4 model, view, projection;
                uniform sampler2D textureSampler;
                void main()
                {
                    float d = 1.0 - texture(textureSampler, vec2(texCoords.x * 0.5 + 0.5, texCoords.y)).r;
                    d -= 0.5;
                    vec3 displaced = position + normal * d;
                    gl_Position = projection * view * model * vec4(displaced, 1.0);
                    fragTexCoords = vec2(texCoords.x * 0.5, texCoords.y);
                }
                """,
                """
                #version 330 core
                in vec2 fragTexCoords;
                out vec4 FragColor;
                uniform sampler2D textureSampler;
                void main()
                {
                    FragColor = texture(textureSampler, fragTexCoords);
                }
                """
            );

            camClient = new StreamClient(URL);
            camClient.Start(width * 2, height, quality, true);

            Program.window.FileDrop += Window_FileDrop;
        }

        private static bool IsVideoFile(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            foreach (var e in VideoExts) if (ext == e) return true;
            return false;
        }

        private void Window_FileDrop(FileDropEventArgs e)
        {
            if (e.FileNames.Length == 0) return;
            string file = e.FileNames[0];
            if (!File.Exists(file) || !IsVideoFile(file)) return;

            // stop camera takeover if active
            cameraReader?.Dispose();
            cameraReader = null;

            // start new video takeover
            videoReader?.Dispose();
            videoReader = new AsyncVideoReader(file);
            camClient.StartTakeover();
        }

        public object Clone() => new RGBDComponent { URL = URL };

        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState kb, float dt)
        {
            if (kb.IsKeyReleased(Keys.Space) && kb.WasKeyDown(Keys.LeftShift))
            {
                if (videoReader != null)
                {
                    videoReader?.Dispose();
                    videoReader = null;
                }

                if (cameraReader == null)
                {
                    cameraReader = new AsyncCameraReader(0);
                    camClient.StartTakeover();
                }
                else
                {
                    cameraReader.Dispose();
                    cameraReader = null;
                }
            }
        }

        public void HandleMouseInput(BaseMesh mesh, MouseState ms, Vector2 delta, bool pressed) { }

        public void Update(BaseMesh mesh, float dt)
        {
            // 1) Send takeover frames – priority: video > camera
            if (videoReader != null)
            {
                nint ptr = videoReader.GetCurrentFramePtr();
                if (ptr != IntPtr.Zero)
                    camClient.SendTakeoverFrame(ptr, videoReader.Width, videoReader.Height);

                if (videoReader.EndOfVideo)
                {
                    videoReader.Dispose();
                    videoReader = null;
                }
            }
            else if (cameraReader != null)
            {
                nint ptr = cameraReader.GetCurrentFramePtr();
                if (ptr != IntPtr.Zero)
                    camClient.SendTakeoverFrame(ptr, cameraReader.Width, cameraReader.Height);
            }

            // 2) Retrieve combined RGB-D frame from server
            using var frame = camClient.Update();
            if (frame == null) return;

            var rect = new Rectangle(0, 0, frame.Width, frame.Height);
            BitmapData data = frame.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);

            cameraTexture?.Dispose();
            cameraTexture = new Texture("rgbd_sidebyside", data.Scan0, frame.Width, frame.Height, swapRB: false);

            frame.UnlockBits(data);

            mesh.Texture = cameraTexture;
            mesh.shader = RGBDShader;

            float aspect = (cameraTexture.width / 2f) / cameraTexture.height;
            mesh.Transform.Scale = new Vector3(aspect, 1, 1);

            Program.window.SetOverrideRGBD(cameraTexture);
        }

        public void Dispose()
        {
            videoReader?.Dispose();
            cameraReader?.Dispose();
            camClient?.Dispose();
            cameraTexture?.Dispose();
        }
    }
}
