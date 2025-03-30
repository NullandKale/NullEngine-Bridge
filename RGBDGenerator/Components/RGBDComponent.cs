// Import GPU-related functionalities for parallel computing and image processing.
using NullEngine.Renderer.Components; // Integrate rendering components from the NullEngine framework
using NullEngine.Renderer.Mesh; // Mesh data structures and transformations for rendering scenes
using NullEngine.Renderer.Scenes; // Scene management components to handle camera and environment data
using NullEngine.Renderer.Shaders; // Shader management for custom GPU-based rendering effects
using NullEngine.Renderer.Textures; // Texture classes for dynamic texture creation and binding
using OpenTK.Mathematics; // Mathematical types (e.g., vectors, matrices) from OpenTK, crucial for graphics transformations
using OpenTK.Windowing.GraphicsLibraryFramework; // GLFW integration through OpenTK for window/input management

namespace RGBDGenerator.Components
{
    /// <summary>
    /// The RGBDComponent class integrates input sources (images, video, live camera)
    /// and draws them with a custom depth-based displacement shader. It relies on
    /// the RGBDAssetHandler to load/process frames and produce the final textures.
    /// </summary>
    public class RGBDComponent : IComponent
    {
        /// <summary>
        /// The texture that is eventually bound to the mesh; dynamically generated from processed RGBD data.
        /// </summary>
        public Texture texture = null;

        /// <summary>
        /// The shader that applies depth-based displacement to vertices during rendering.
        /// </summary>
        public Shader RGBDShader;

        // The rendering mode: 0 for color, 1 for debug depth, 2 for composite view.
        private int mode = 0;

        // Stores the current aspect ratio value (which can be adjusted manually).
        private float aspectRatioOverride = 0.0f;

        // Flag that indicates whether the aspect ratio has been manually adjusted.
        private bool manualAspectRatio = false;

        // --- Depth parameters for controlling the visual effect in the shader ---
        private float depthScale = 1.0f;    // Controls overall depth effect strength
        private float depthBias = 0.6f;     // Shifts the "zero" depth plane up or down
        private float depthPower = 1.0f;    // Non-linear exponent for adjusting depth falloff

        // For advanced usage: if we need to pause or play a loaded video
        private bool videoPlaying = true;

        // The asset handler that loads and processes actual images and frames
        private RGBDAssetHandler assetHandler;

        /// <summary>
        /// Constructor. Initializes the depth generator, asset handler, and sets up the custom RGBD shader.
        /// Also hooks the FileDrop event so we can drag-and-drop images or videos.
        /// </summary>
        public RGBDComponent()
        {
            // Create and configure our asset handler
            assetHandler = new RGBDAssetHandler("Assets/input.png");

            // Register file drop event on the main window so the asset handler can process new files
            // (Replace 'Program.window' with your actual OpenTK window reference.)
            assetHandler.SubscribeToFileDrop(Program.window);

            // Build the custom shader for RGBD rendering, blending color and depth channels.
            RGBDShader = new Shader(
                // Vertex shader
                @"
                #version 330 core
                layout (location = 0) in vec3 position;
                layout (location = 1) in vec3 normal;
                layout (location = 2) in vec2 texCoords;
                out vec2 fragTexCoords;
                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;
                uniform sampler2D textureSampler;
                uniform int mode;
                uniform float depthScale;
                uniform float depthBias;
                uniform float depthPower;

                vec2 getDepthTexCoord(vec2 compTexCoord, int mode)
                {
                    // If in composite mode, the 'depth' portion may be on the right half
                    if (mode == 2)
                    {
                        if (compTexCoord.x < 0.5)
                            return vec2(compTexCoord.x + 0.5, compTexCoord.y);
                        else
                            return compTexCoord;
                    }
                    else
                    {
                        // Standard remap: depth is assumed to be in the right half
                        return vec2(compTexCoord.x * 0.5 + 0.5, compTexCoord.y);
                    }
                }

                vec2 getColorTexCoord(vec2 compTexCoord, int mode)
                {
                    if (mode == 0)
                    {
                        // Color in the left half
                        return vec2(compTexCoord.x * 0.5, compTexCoord.y);
                    }
                    else if (mode == 1)
                    {
                        // Debug depth in the right half
                        return vec2(compTexCoord.x * 0.5 + 0.5, compTexCoord.y);
                    }
                    else
                    {
                        // Composite uses the full span
                        return compTexCoord;
                    }
                }

                void main()
                {
                    // Sample the raw depth from the portion of the texture that contains depth
                    vec2 depthCoord = getDepthTexCoord(texCoords, mode);
                    float rawDepth = texture(textureSampler, depthCoord).r;

                    // Non-linear emphasis
                    float adjustedDepth = pow(rawDepth, depthPower);

                    // Final displacement (1 - depth) to invert sense of nearer/farther
                    float d = (1.0 - adjustedDepth) * depthScale - depthBias;

                    // Displace vertices along the normal
                    vec3 displacedPos = position + normal * d;

                    gl_Position = projection * view * model * vec4(displacedPos, 1.0);

                    // For the color portion, pick which half or which subset of the texture is used
                    fragTexCoords = getColorTexCoord(texCoords, mode);
                }
                ",

                // Fragment shader
                @"
                #version 330 core
                in vec2 fragTexCoords;
                out vec4 FragColor;
                uniform sampler2D textureSampler;

                void main()
                {
                    FragColor = texture(textureSampler, fragTexCoords);
                }
                "
            );

            // Set initial values for the depth-based displacement in the shader
            RGBDShader.SetUniform("depthScale", depthScale);
            RGBDShader.SetUniform("depthBias", depthBias);
            RGBDShader.SetUniform("depthPower", depthPower);
        }

        /// <summary>
        /// Creates a shallow clone of this component with copied configuration.
        /// The asset handler is freshly initialized, but we copy over the same
        /// depth values and aspect ratio settings.
        /// </summary>
        public object Clone()
        {
            RGBDComponent clone = new RGBDComponent();
            // Copy depth settings
            clone.depthScale = depthScale;
            clone.depthBias = depthBias;
            clone.depthPower = depthPower;
            clone.RGBDShader.SetUniform("depthScale", depthScale);
            clone.RGBDShader.SetUniform("depthBias", depthBias);
            clone.RGBDShader.SetUniform("depthPower", depthPower);

            // Copy aspect ratio overrides
            clone.aspectRatioOverride = aspectRatioOverride;
            clone.manualAspectRatio = manualAspectRatio;

            // Note: This does not copy the actual texture or streaming state;
            // you can customize as needed for your application.
            return clone;
        }

        /// <summary>
        /// Handles keyboard input (toggling depth parameters, video controls, camera selection).
        /// Param tweaks (depth scale, bias, power) are sent directly to the shader to allow
        /// real-time updates.
        /// </summary>
        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {
            // Adjust depth scale with T/G
            if (keyboardState.IsKeyDown(Keys.T))
            {
                depthScale += deltaTime * 0.5f;
                RGBDShader.SetUniform("depthScale", depthScale);
            }
            if (keyboardState.IsKeyDown(Keys.G))
            {
                depthScale = Math.Max(0.1f, depthScale - deltaTime * 0.5f);
                RGBDShader.SetUniform("depthScale", depthScale);
            }

            // Adjust depth bias with Y/H
            if (keyboardState.IsKeyDown(Keys.Y))
            {
                depthBias += deltaTime * 0.2f;
                RGBDShader.SetUniform("depthBias", depthBias);
            }
            if (keyboardState.IsKeyDown(Keys.H))
            {
                depthBias -= deltaTime * 0.2f;
                RGBDShader.SetUniform("depthBias", depthBias);
            }

            // Adjust depth power with U/J
            if (keyboardState.IsKeyDown(Keys.U))
            {
                depthPower += deltaTime * 0.3f;
                RGBDShader.SetUniform("depthPower", depthPower);
            }
            if (keyboardState.IsKeyDown(Keys.J))
            {
                depthPower = Math.Max(0.1f, depthPower - deltaTime * 0.3f);
                RGBDShader.SetUniform("depthPower", depthPower);
            }

            // Toggle playback of a video
            // (The asset handler doesn't directly handle pause/resume, but you can add if needed.)
            if (!assetHandler.usingCamera && keyboardState.IsKeyPressed(Keys.Space))
            {
                // Example placeholder for toggling videoPlaying state
                videoPlaying = !videoPlaying;
                // If using your custom videoReader, you can call .Pause() or .Play() on it here
            }

            // Key R could restart the video if integrated fully with AsyncVideoReader (omitted for brevity)

            // Toggle recording with key V
            if (keyboardState.IsKeyPressed(Keys.V))
            {
                assetHandler.ToggleRecording();
            }

            // Toggle rendering mode with key M
            if (keyboardState.IsKeyPressed(Keys.M))
            {
                mode = (mode + 1) % 3;
            }

            // Switch camera inputs with number keys
            if (keyboardState.IsKeyPressed(Keys.D1))
            {
                assetHandler.DisposeCurrentStream();
                assetHandler.OpenCamera(0);
            }
            if (keyboardState.IsKeyPressed(Keys.D2))
            {
                assetHandler.DisposeCurrentStream();
                assetHandler.OpenCamera(1);
            }
            if (keyboardState.IsKeyPressed(Keys.D3))
            {
                assetHandler.DisposeCurrentStream();
                assetHandler.OpenCamera(2);
            }
            if (keyboardState.IsKeyPressed(Keys.D4))
            {
                assetHandler.DisposeCurrentStream();
                assetHandler.OpenCamera(3);
            }
            if (keyboardState.IsKeyPressed(Keys.D5))
            {
                assetHandler.DisposeCurrentStream();
                assetHandler.OpenCamera(4);
            }

            // Adjust FOV with I/K
            if (keyboardState.IsKeyDown(Keys.I))
            {
                float newFov = SceneManager.GetActiveScene().fov + deltaTime * 10.0f;
                SceneManager.GetActiveScene().UpdateFieldOfView(newFov);
            }
            if (keyboardState.IsKeyDown(Keys.K))
            {
                float newFov = SceneManager.GetActiveScene().fov - deltaTime * 10.0f;
                SceneManager.GetActiveScene().UpdateFieldOfView(newFov);
            }

            // Move mesh along Z with O/L
            if (keyboardState.IsKeyDown(Keys.O))
            {
                mesh.Transform.Position += new Vector3(0, 0, deltaTime * 1.0f);
            }
            if (keyboardState.IsKeyDown(Keys.L))
            {
                mesh.Transform.Position += new Vector3(0, 0, -deltaTime * 1.0f);
            }

            // Aspect ratio override with A/Z
            if (keyboardState.IsKeyDown(Keys.A))
            {
                aspectRatioOverride += deltaTime * 0.5f;
                manualAspectRatio = true;
            }
            if (keyboardState.IsKeyDown(Keys.Z))
            {
                aspectRatioOverride -= deltaTime * 0.5f;
                manualAspectRatio = true;
            }

            // Reset with C
            if (keyboardState.IsKeyPressed(Keys.C))
            {
                if (texture != null)
                {
                    // Half width (since one half for color, the other for depth in single-mode)
                    aspectRatioOverride = (texture.width / 2f) / texture.height;
                }
                else
                {
                    aspectRatioOverride = 1.0f;
                }
                manualAspectRatio = false;
                SceneManager.GetActiveScene().UpdateFieldOfView(14.0f);

                // Reset depth parameters
                depthScale = 1.0f;
                depthBias = 0.0f;
                depthPower = 0.8f;
                RGBDShader.SetUniform("depthScale", depthScale);
                RGBDShader.SetUniform("depthBias", depthBias);
                RGBDShader.SetUniform("depthPower", depthPower);
            }
        }

        /// <summary>
        /// Currently unused for this example, but can be extended to handle mouse interactions with the mesh.
        /// </summary>
        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {
            // Implementation omitted - can be used for object rotation, dragging, etc.
        }

        /// <summary>
        /// Per-frame update routine. Retrieves the latest texture from the asset handler
        /// (whether from static image, video, or camera), updates the mesh, and configures
        /// the scale based on mode (color/debug/composite).
        /// </summary>
        public void Update(BaseMesh mesh, float deltaTime)
        {
            Texture newTexture = assetHandler.GetLatestTexture();
            if (newTexture != null)
            {
                // Dispose the old texture to free GPU memory
                if (texture != null) texture.Dispose();
                texture = newTexture;
            }

            // Update mesh scaling based on computed or overridden aspect ratio
            if (texture != null)
            {
                // Bind the texture and the RGBD shader to the mesh
                mesh.Texture = texture;
                mesh.shader = RGBDShader;
                RGBDShader.SetUniform("mode", mode);

                if (mode == 2)
                {
                    // Composite mode uses the full width
                    float compositeAspect = (float)texture.width / (float)texture.height;
                    mesh.Transform.Scale = new Vector3(compositeAspect * 0.5f, 0.5f, 0.5f);
                }
                else
                {
                    // Color or debug mode uses half-width
                    float computedAspectRatio = (texture.width / 2f) / texture.height;
                    if (!manualAspectRatio)
                    {
                        aspectRatioOverride = computedAspectRatio;
                    }
                    mesh.Transform.Scale = new Vector3(aspectRatioOverride, 1, 1);
                }
            }
        }
    }
}
