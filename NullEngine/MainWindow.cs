using System;
using OpenTK.Windowing.Desktop;
using OpenTK.Windowing.Common;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using BridgeSDK;
using System.Linq;
using NullEngine;
using NullEngine.Renderer.Components;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System.Collections.Generic;
using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Shaders;
using NullEngine.Renderer.Textures;
using NullEngine.Renderer.Scenes;
using NullEngine.Utils;

namespace NullEngine
{
    public class MainWindow : GameWindow
    {
        // Bridge Controller
        private BridgeSDK.Window wnd = 0;
        private BridgeWindowData bridgeData;
        private bool isBridgeDataInitialized = false;

        // Framebuffers
        private Framebuffer primaryFramebuffer;
        private Framebuffer quiltFramebuffer;

        // Variables for mouse control
        private bool isMiddleMouseCaptured = false;
        private bool mousePressed = false;
        private Vector2 lastMousePos;

        private FpsCounter fpsCounter = new FpsCounter();

        public MainWindow()
            : base(GameWindowSettings.Default, new NativeWindowSettings()
            {
                Size = new Vector2i(800, 800),
                Title = "Null Engine",
            })
        {
        }


        // Virtual methods to provide scenes and scene index
        protected virtual (string SceneFilePath, string ActiveSceneName)[] GetScenes()
        {
            // Default scenes
            return new[]
            {
                ("Assets/Scenes/TestScene.json", "TestScene0"),
            };
        }

        protected virtual int GetSceneIndex()
        {
            // Default scene index
            return 0;
        }

        protected override void OnLoad()
        {
            ShaderManager.LoadShaders();
            MeshManager.LoadMeshes();

            if (!Controller.Initialize("BridgeSDKSampleNative"))
            {
                Log.Debug("Failed to initialize bridge. Bridge may be missing, or the version may be too old");
            }

            List<DisplayInfo> displays = Controller.GetDisplayInfoList();

            if (displays.Count > 0 && Controller.InstanceWindowGL(ref wnd, displays[0].DisplayId))
            {
                bridgeData = Controller.GetWindowData(wnd);
                isBridgeDataInitialized = (bridgeData.Wnd != 0);
            }
            else
            {
                Log.Debug("No display connected");
            }

            if (isBridgeDataInitialized)
            {
                int window_width = (int)bridgeData.OutputWidth / 2;
                int window_height = (int)bridgeData.OutputHeight / 2;
                Size = new Vector2i(window_width, window_height);

                // Initialize framebuffers using the Framebuffer class
                primaryFramebuffer = new Framebuffer(Size.X, Size.Y);
                quiltFramebuffer = new Framebuffer((int)bridgeData.QuiltWidth, (int)bridgeData.QuiltHeight);
            }

            // Use the virtual methods to load scenes
            (string SceneFilePath, string ActiveSceneName)[] scenes = GetScenes();
            int sceneIndex = GetSceneIndex();

            if (sceneIndex >= 0 && sceneIndex < scenes.Length)
            {
                var sceneInfo = scenes[sceneIndex];
                string sceneFilePath = sceneInfo.SceneFilePath;
                string activeSceneName = sceneInfo.ActiveSceneName;

                try
                {
                    SceneLoader.LoadScenesFromJson(sceneFilePath, bridgeData);
                    Log.Debug($"Loaded scenes from {sceneFilePath}.");

                    SceneManager.SetActiveScene(activeSceneName);
                    Log.Debug($"Set active scene to '{activeSceneName}'.");
                }
                catch (Exception ex)
                {
                    Log.Debug($"Error loading scenes from {sceneFilePath}: {ex.Message}");
                }
            }
            else
            {
                Log.Debug($"Invalid scene index: {sceneIndex}. No matching scene configuration found.");
            }


            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.Enable(EnableCap.DepthTest);
            GL.PolygonMode(MaterialFace.Front, PolygonMode.Line);
            GL.ClearColor(0.0f, 0.0f, 0.0f, 1.0f);

            base.OnLoad();
        }

        protected override void OnUpdateFrame(FrameEventArgs args)
        {
            Scene activeScene = SceneManager.GetActiveScene();

            // Handle mouse input
            if (MouseState.IsButtonDown(MouseButton.Left))
            {
                if (!mousePressed)
                {
                    mousePressed = true;
                    lastMousePos = MouseState.Position;
                }
                else
                {
                    Vector2 delta = MouseState.Position - lastMousePos;
                    lastMousePos = MouseState.Position;

                    activeScene?.HandleMouseInput(MouseState, delta, true);
                }
            }
            else if (mousePressed)
            {
                activeScene?.HandleMouseInput(MouseState, Vector2.Zero, false);
                mousePressed = false;
            }

            // Toggle middle mouse capture
            if (MouseState.IsButtonPressed(MouseButton.Middle))
            {
                isMiddleMouseCaptured = !isMiddleMouseCaptured;

                if (isMiddleMouseCaptured)
                {
                    // Capture the mouse
                    CursorState = CursorState.Grabbed;
                    Log.Debug("Middle mouse captured.");
                }
                else
                {
                    // Release the mouse
                    CursorState = CursorState.Normal;
                    Log.Debug("Middle mouse released.");
                }
            }

            if (KeyboardState.IsKeyReleased(Keys.P))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"quilt_{timestamp}_qs{bridgeData.Vx}x{bridgeData.Vy}a{bridgeData.DisplayAspect}.png";
                quiltFramebuffer.Capture(filename);
                Log.Debug($"Captured screenshot: {filename}");
            }


            activeScene?.HandleKeyboardInput(KeyboardState, (float)args.Time);

            // Update the scene
            activeScene?.Update((float)args.Time);
            base.OnUpdateFrame(args);
        }

        protected override void OnRenderFrame(FrameEventArgs args)
        {
            fpsCounter.Update((float)args.Time);

            // Update window title with FPS stats
            Title = $"Null Engine | Last Frame Time: {fpsCounter.GetLastFrameTimeMs():0.00} ms | " +
                           $"Avg FPS (1s): {fpsCounter.GetAverageFps1Sec():0.0} | " +
                           $"Avg FPS (5s): {fpsCounter.GetAverageFps5Sec():0.0} | " +
                           $"Min FPS: {fpsCounter.GetMinFps():0.0}";

            // Render to the default framebuffer (primary window)
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0); // Default framebuffer
            GL.Viewport(0, 0, Size.X, Size.Y);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            // Render the active scene directly to the primary window
            Scene activeScene = SceneManager.GetActiveScene();
            activeScene?.Render();

            Context.SwapBuffers();

            if (isBridgeDataInitialized)
            {
                // Render to the quilt framebuffer
                quiltFramebuffer.Bind();
                GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

                int totalViews = bridgeData.Vx * bridgeData.Vy;
                for (int y = 0; y < bridgeData.Vy; y++)
                {
                    for (int x = 0; x < bridgeData.Vx; x++)
                    {
                        int invertedY = (int)bridgeData.Vy - 1 - y;
                        GL.Viewport(
                            x * (int)bridgeData.ViewWidth,
                            invertedY * (int)bridgeData.ViewHeight,
                            (int)bridgeData.ViewWidth,
                            (int)bridgeData.ViewHeight);

                        int viewIndex = y * (int)bridgeData.Vx + x;
                        float normalizedView = (float)viewIndex / (float)(totalViews - 1);
                        activeScene?.Render(normalizedView, true);
                    }
                }

                quiltFramebuffer.Unbind();

                // Use the quilt framebuffer's texture for the Bridge SDK interop
                Controller.DrawInteropQuiltTextureGL(
                    bridgeData.Wnd,
                    (ulong)quiltFramebuffer.TextureId,
                    PixelFormats.RGBA,
                    (uint)bridgeData.QuiltWidth,
                    (uint)bridgeData.QuiltHeight,
                    (uint)bridgeData.Vx,
                    (uint)bridgeData.Vy,
                    bridgeData.DisplayAspect,
                    1.0f);
            }

            base.OnRenderFrame(args);
        }

        protected override void OnUnload()
        {
            Controller.Uninitialize();
            primaryFramebuffer?.Cleanup();
            quiltFramebuffer?.Cleanup();
            base.OnUnload();
        }
    }
}
