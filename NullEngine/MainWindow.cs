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
    public class OverrideRGBD
    {
        public Texture texture;
        public int quiltRows;
        public int quiltCols;
        public int quiltWidth;
        public int quiltHeight;
        public float quiltAspect;
    }

    public class OverrideQuilt
    {
        // Mark these public if you want to set/read them directly:
        public Texture texture;
        public int quiltRows;
        public int quiltCols;
        public float quiltAspect;
    }

    public class MainWindow : GameWindow
    {
        // Bridge Controller
        private BridgeSDK.Window wnd = 0;
        private BridgeWindowData bridgeData;
        private bool isBridgeDataInitialized = false;

        // Framebuffers
        private Framebuffer primaryFramebuffer;
        private Framebuffer quiltFramebuffer;

        // Two distinct overrides:
        private OverrideRGBD overrideRGBD = null;
        private OverrideQuilt overrideQuilt = null;

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

            if (!Controller.InitializeWithPath("BridgeSDKSampleNative", "C:\\Users\\alec\\source\\repos\\LookingGlassBridge\\out\\build\\x64-Debug"))
            {
                Log.Debug("Failed to initialize bridge. Bridge may be missing, or the version may be too old");
            }

            //if (!Controller.Initialize("BridgeSDKSampleNative"))
            //{
            //    Log.Debug("Failed to initialize bridge. Bridge may be missing, or the version may be too old");
            //}

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

            // Load scenes
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

        private void MainWindow_FileDrop(FileDropEventArgs obj)
        {
            // ...
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
                    CursorState = CursorState.Grabbed;
                    Log.Debug("Middle mouse captured.");
                }
                else
                {
                    CursorState = CursorState.Normal;
                    Log.Debug("Middle mouse released.");
                }
            }

            // Screenshot test
            if (KeyboardState.IsKeyReleased(Keys.P))
            {
                string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                string filename = $"quilt_{timestamp}_qs{bridgeData.Vx}x{bridgeData.Vy}a{bridgeData.DisplayAspect}.png";
                quiltFramebuffer.Capture(filename);
                Log.Debug($"Captured screenshot: {filename}");
            }

            // Keyboard + scene updates
            activeScene?.HandleKeyboardInput(KeyboardState, (float)args.Time);
            activeScene?.Update((float)args.Time);

            // Update video textures
            TextureManager.UpdateVideoTextures((float)args.Time);

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

            // 1) Render to the primary window
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.Viewport(0, 0, Size.X, Size.Y);
            GL.Clear(ClearBufferMask.ColorBufferBit | ClearBufferMask.DepthBufferBit);

            Scene activeScene = SceneManager.GetActiveScene();
            activeScene?.Render();

            Context.SwapBuffers();

            // 2) If Bridge is initialized, also render the quilt
            if (isBridgeDataInitialized)
            {
                // 3) Choose whether to call DrawInteropRGBD or DrawInteropQuilt
                //    If overrideRGBD is set, we do that first. If not, check overrideQuilt.
                if (overrideRGBD != null && overrideRGBD.texture != null)
                {
                    // Use the Overridden RGBD texture
                    Controller.DrawInteropRGBDTextureGL(
                        bridgeData.Wnd,
                        (ulong)overrideRGBD.texture.textureId,
                        PixelFormats.RGBA,
                        (uint)overrideRGBD.texture.width,
                        (uint)overrideRGBD.texture.height,
                        (uint)overrideRGBD.quiltWidth, 
                        (uint)overrideRGBD.quiltHeight,
                        (uint)overrideRGBD.quiltCols,   // layout in columns
                        (uint)overrideRGBD.quiltRows,   // layout in rows
                        bridgeData.DisplayAspect,       // aspect ratio for the final display
                        activeScene.Focus * 0.05f,
                        activeScene.Offset,
                        1.0f, 2                            // zoom, depth position
                    );
                }
                else if (overrideQuilt != null && overrideQuilt.texture != null)
                {
                    // Use an Overridden Quilt
                    // We'll pull the actual texture size from overrideQuilt.texture.
                    // If you store them separately, adjust as needed.
                    uint texW = (uint)overrideQuilt.texture.width;
                    uint texH = (uint)overrideQuilt.texture.height;

                    Controller.DrawInteropQuiltTextureGL(
                        bridgeData.Wnd,
                        (ulong)overrideQuilt.texture.textureId,
                        PixelFormats.RGBA,
                        texW,
                        texH,
                        (uint)overrideQuilt.quiltCols,
                        (uint)overrideQuilt.quiltRows,
                        overrideQuilt.quiltAspect,
                        1.0f
                    );
                }
                else
                {
                    // Render the standard quilt (all views) into quiltFramebuffer
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
                            float normalizedView = (float)viewIndex / (totalViews - 1);
                            activeScene?.Render(normalizedView, true);
                        }
                    }

                    quiltFramebuffer.Unbind();

                    // No override; use the default quilt FBO
                    Controller.DrawInteropQuiltTextureGL(
                        bridgeData.Wnd,
                        (ulong)quiltFramebuffer.TextureId,
                        PixelFormats.RGBA,
                        (uint)bridgeData.QuiltWidth,
                        (uint)bridgeData.QuiltHeight,
                        (uint)bridgeData.Vx,
                        (uint)bridgeData.Vy,
                        bridgeData.DisplayAspect,
                        1.0f
                    );
                }
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

        // -------------------------------------------------------------------
        //  Override setup methods
        // -------------------------------------------------------------------

        public void SetOverrideRGBD(Texture texture, float override_aspect = -1f)
        {
            if (bridgeData == null)
            {
                return;
            }

            overrideRGBD = new OverrideRGBD
            {
                texture = texture,
                quiltWidth = bridgeData.QuiltWidth,
                quiltHeight = bridgeData.QuiltHeight,
                quiltRows = bridgeData.Vy,
                quiltCols = bridgeData.Vx,
                quiltAspect = override_aspect <= 0 ? texture.width / (float)texture.height : override_aspect,
            };
            // Clear out any quilt override so we don’t conflict
            overrideQuilt = null;
        }

        /// <summary>
        /// Override the quilt rendering with an RGBD “quilt.” 
        /// (Calls DrawInteropRGBDTextureGL.)
        /// </summary>
        public void SetOverrideRGBD(Texture texture, int quiltWidth, int quiltHeight,
                                     int quiltRows, int quiltCols, float aspect)
        {
            overrideRGBD = new OverrideRGBD
            {
                texture = texture,
                quiltWidth = quiltWidth,
                quiltHeight = quiltHeight,
                quiltRows = quiltRows,
                quiltCols = quiltCols,
                quiltAspect = aspect
            };
            // Clear out any quilt override so we don’t conflict
            overrideQuilt = null;
        }

        /// <summary>
        /// Override the standard quilt with a custom quilt layout and texture.
        /// (Calls DrawInteropQuiltTextureGL.)
        /// </summary>
        public void SetOverrideQuilt(Texture texture, int quiltRows, int quiltCols, float aspect)
        {
            overrideQuilt = new OverrideQuilt
            {
                texture = texture,
                quiltRows = quiltRows,
                quiltCols = quiltCols,
                quiltAspect = aspect
            };
            // Clear out the RGBD override so we don’t conflict
            overrideRGBD = null;
        }

        /// <summary>
        /// Disables both overrides, returning to normal quilt rendering.
        /// </summary>
        public void ClearOverride()
        {
            overrideRGBD = null;
            overrideQuilt = null;
        }
    }
}
