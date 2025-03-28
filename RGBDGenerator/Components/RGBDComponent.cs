using GPU;
using ILGPU.Runtime.Cuda;
using LKG_NVIDIA_RAYS.Utils;
using NullEngine.Renderer.Components;
using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Scenes;
using NullEngine.Renderer.Shaders;
using NullEngine.Renderer.Textures;
using NullEngine.Utils;
using OpenTK.Mathematics;
using OpenTK.Windowing.Common.Input;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using static OpenCvSharp.Stitcher;
using static System.Net.Mime.MediaTypeNames;

namespace RGBDGenerator.Components
{
    // RGBDComponent integrates image, video, and live camera inputs by converting each frame into an RGBD image.
    // It uses dedicated readers (AsyncVideoReader or AsyncCameraReader) to obtain frames,
    // then computes depth via a DepthGenerator, and finally applies the resulting texture along with a custom shader to a mesh.
    public class RGBDComponent : IComponent
    {
        // The file path for image-based input (loaded from configuration); only used when not in live mode.
        public string Filename;
        // The texture that is eventually bound to the mesh – dynamically generated from RGBD data.
        public Texture texture = null;
        // The shader that applies depth-based displacement to vertices during rendering.
        public Shader RGBDShader;
        // Temporarily holds a filename from a drag-and-drop event until it can be processed.
        private string pendingFilename;
        // Handles depth inference on a GPU using an ONNX model.
        private DepthGenerator depthGenerator;

        // --- Video/Camera input fields ---
        // Indicates if the current file stream should be interpreted as a video (based on its extension).
        private bool isVideo = false;
        // Flags that a live camera feed is active instead of a file stream.
        private bool usingCamera = false;
        // Indicates that the video file has reached its end, so further frame processing is suspended.
        private bool videoFinished = false;
        // Reads frames sequentially from a video file; supports both automatic and single-frame modes.
        private AsyncVideoReader videoReader = null;
        // Reads frames from a connected camera device.
        private AsyncCameraReader cameraReader = null;
        // When recording is enabled, this writes each processed frame to disk.
        private VideoWriter videoWriter = null;
        // Temporarily stores a GPUImage instance to avoid repeated allocations for video frame conversion.
        private GPUImage tempVideoFrame;
        // Maintains the current play/pause state of the video stream.
        private bool videoPlaying = true;
        private int mode = 0;

        // --- New fields for aspect ratio override ---
        // Stores the current aspect ratio value (which can be adjusted manually).
        private float aspectRatioOverride = 0.0f;
        // Flag that indicates whether the aspect ratio has been manually adjusted.
        private bool manualAspectRatio = false;

        // --- New fields for enhanced depth control ---
        private float depthScale = 2.0f;      // Controls overall depth effect strength
        private float depthBias = 0.1f;       // Adjusts the "zero point" of depth 
        private float depthPower = 1.2f;      // Non-linear power for depth emphasis

        public RGBDComponent()
        {
            // Initialize file source as empty and texture as unassigned.
            Filename = "";
            texture = null;

            // Choose an inference resolution for depth computation; here, 720 is selected as a balanced trade-off.
            int inferenceSize = 512;
            // Uncomment one of the following lines to select a specific ONNX model.

            depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-small_fp16.onnx");
            //depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-small.onnx");

            //depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-base_q4f16.onnx");
            //depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-base_fp16.onnx");
            //depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-base.onnx");

            //depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-large_q4f16.onnx");
            //depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-large_q4.onnx");
            //depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-large_fp16.onnx");
            //depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-large.onnx");

            // Hook into the window's file-drop event so that externally dropped files are automatically processed.
            Program.window.FileDrop += FileDrop;

            // Build a custom shader for RGBD rendering with enhanced depth controls
            RGBDShader = new Shader(
                // IMPROVED Vertex shader source with enhanced depth controls
                @"
                #version 330 core
                // Per-vertex input attributes.
                layout (location = 0) in vec3 position;
                layout (location = 1) in vec3 normal;
                layout (location = 2) in vec2 texCoords;
                // Pass texture coordinates to fragment shader.
                out vec2 fragTexCoords;
                // Uniform matrices for transforming vertex positions.
                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;
                // Texture sampler for accessing RGBD data.
                uniform sampler2D textureSampler;
                uniform int mode;
                
                // Enhanced depth control uniforms
                uniform float depthScale;    // Overall depth effect strength
                uniform float depthBias;     // Shifts zero point of depth
                uniform float depthPower;    // Non-linear transformation power
                
                void main()
                {
                    // Sample depth from right half of texture (depth map)
                    float rawDepth = texture(textureSampler, vec2(texCoords.x * 0.5 + 0.5, texCoords.y)).r;
                    
                    // Apply non-linear transformation to emphasize certain depth ranges
                    float adjustedDepth = pow(rawDepth, depthPower);
                    
                    // Enhanced depth calculation with scale and bias
                    float depthVal = (1.0 - adjustedDepth) * depthScale - depthBias;
                    
                    // Displace the vertex along its normal by the computed depth value.
                    vec3 displacedPos = position + normal * depthVal;
                    
                    // Transform the displaced vertex to clip space.
                    gl_Position = projection * view * model * vec4(displacedPos, 1.0);
                    
                    // Adjust texture coordinates for the fragment shader.
                    if(mode == 0)
                    {
                        // color output (left half)
                        fragTexCoords = vec2(texCoords.x * 0.5, texCoords.y);
                    }
                    else if(mode == 1)
                    {
                        // depth output for debugging (right half)
                        fragTexCoords = vec2(texCoords.x * 0.5 + 0.5, texCoords.y);
                    }
                }
                ",
                // Fragment shader source - unchanged
                @"
                #version 330 core
                in vec2 fragTexCoords;
                // Final output color.
                out vec4 FragColor;
                // Texture sampler for RGBD texture.
                uniform sampler2D textureSampler;

                void main()
                {
                    // Output the texture color directly.
                    FragColor = texture(textureSampler, fragTexCoords);
                }
                ");

            // Initialize depth control uniforms
            RGBDShader.SetUniform("depthScale", depthScale);
            RGBDShader.SetUniform("depthBias", depthBias);
            RGBDShader.SetUniform("depthPower", depthPower);
        }

        // Event handler for processing file drops.
        // Determines whether the dropped file is a video (based on its extension) and prepares it for later loading.
        private void FileDrop(OpenTK.Windowing.Common.FileDropEventArgs obj)
        {
            if (obj.FileNames.Length > 0 && File.Exists(obj.FileNames[0]))
            {
                // Store the file path until the next update cycle.
                pendingFilename = obj.FileNames[0];
                // Determine if the file should be handled as a video by inspecting its extension.
                string ext = Path.GetExtension(pendingFilename).ToLower();
                isVideo = (ext == ".mp4" || ext == ".avi" || ext == ".mov");
                // Reset streaming flags when a new file is provided.
                usingCamera = false;
                videoFinished = false;
            }
        }

        // Converts a static image file into an RGBD Bitmap.
        // Loads the file into GPU memory, computes depth, and then converts the result back to a Bitmap.
        private Bitmap GenerateRGBDImageFromRGBImage(string filename)
        {
            if (GPUImage.TryLoad(filename, out GPUImage loadedImage))
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                GPUImage output = depthGenerator.ComputeDepth(loadedImage, true);
                stopwatch.Stop();
                loadedImage.Dispose();
                return output.GetBitmap();
            }
            else
            {
                return null;
            }
        }

        // Processes an RGB bitmap (from video or camera) to produce an RGBD image.
        // Reuses an existing GPUImage if dimensions match, or allocates a new one if necessary.
        private GPUImage GenerateRGBDImageFromRGBBitmap(int width, int height, IntPtr rgbaData)
        {
            if (tempVideoFrame == null || tempVideoFrame.width != width || tempVideoFrame.height != height)
            {
                tempVideoFrame = new GPUImage(width, height, rgbaData);
            }
            else
            {
                tempVideoFrame.fromCPU_UNSAFE(rgbaData);
            }
            GPUImage output = depthGenerator.ComputeDepth(tempVideoFrame);
            output.toCPU();
            return output;
        }

        // Converts a Bitmap into a Texture by locking its bits and passing the pixel data pointer.
        private Texture CreateTextureFromBitmap(Bitmap bmp)
        {
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
            Texture tex = new Texture("RGBD", bmpData.Scan0, bmp.Width, bmp.Height, false);
            bmp.UnlockBits(bmpData);
            return tex;
        }

        // Creates a shallow clone of this component.
        public object Clone()
        {
            RGBDComponent clone = new RGBDComponent();
            clone.Filename = Filename;
            // Copy depth settings
            clone.depthScale = depthScale;
            clone.depthBias = depthBias;
            clone.depthPower = depthPower;
            return clone;
        }

        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {
            // -- NEW: Depth parameter controls --
            // Depth Scale adjustment
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

            // Depth Bias adjustment
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

            // Depth Power adjustment
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

            // -- EXISTING CONTROLS -- 
            // Video playback controls (when not using camera)
            if (!usingCamera && videoReader != null)
            {
                if (keyboardState.IsKeyPressed(Keys.Space))
                {
                    if (videoPlaying)
                    {
                        videoReader.Pause();
                        videoPlaying = false;
                    }
                    else
                    {
                        videoReader.Play();
                        videoPlaying = true;
                    }
                }
                if (keyboardState.IsKeyPressed(Keys.R))
                {
                    videoReader.Stop();
                    videoReader.Play();
                    videoPlaying = true;
                }
                // Changed recording toggle from 'Y' to 'V' to avoid conflict with depth bias control.
                if (keyboardState.IsKeyPressed(Keys.V))
                {
                    videoReader.Stop();
                    videoReader.Dispose();
                    videoReader = new AsyncVideoReader(Filename, true, true);
                    videoPlaying = true;

                    if (videoWriter != null)
                    {
                        videoWriter.Dispose();
                        videoWriter = null;
                    }
                    string outputFilename = "output_" + Path.GetFileName(Filename);
                    videoWriter = new VideoWriter(outputFilename, videoReader.Fps, videoReader.Width * 2, videoReader.Height);
                }
            }

            // Mode toggle
            if (keyboardState.IsKeyPressed(Keys.M))
            {
                mode = (mode + 1) % 2;
            }

            // Camera selection
            if (keyboardState.IsKeyPressed(Keys.D1))
            {
                DisposeCurrentStream();
                cameraReader = new AsyncCameraReader(0, true, true);
                usingCamera = true;
                videoFinished = false;
            }
            else if (keyboardState.IsKeyPressed(Keys.D2))
            {
                DisposeCurrentStream();
                cameraReader = new AsyncCameraReader(1, true, true);
                usingCamera = true;
                videoFinished = false;
            }
            else if (keyboardState.IsKeyPressed(Keys.D3))
            {
                DisposeCurrentStream();
                cameraReader = new AsyncCameraReader(2, true, true);
                usingCamera = true;
                videoFinished = false;
            }
            else if (keyboardState.IsKeyPressed(Keys.D4))
            {
                DisposeCurrentStream();
                cameraReader = new AsyncCameraReader(3, true, true);
                usingCamera = true;
                videoFinished = false;
            }
            else if (keyboardState.IsKeyPressed(Keys.D5))
            {
                DisposeCurrentStream();
                cameraReader = new AsyncCameraReader(4, true, true);
                usingCamera = true;
                videoFinished = false;
            }

            // Field of view adjustment
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

            // Z-position adjustment
            if (keyboardState.IsKeyDown(Keys.O))
            {
                mesh.Transform.Position = new Vector3(
                    mesh.Transform.Position.X,
                    mesh.Transform.Position.Y,
                    mesh.Transform.Position.Z + deltaTime * 1.0f);
            }
            if (keyboardState.IsKeyDown(Keys.L))
            {
                mesh.Transform.Position = new Vector3(
                    mesh.Transform.Position.X,
                    mesh.Transform.Position.Y,
                    mesh.Transform.Position.Z - deltaTime * 1.0f);
            }

            // Aspect ratio adjustment
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

            // Reset all adjustable values
            if (keyboardState.IsKeyPressed(Keys.C))
            {
                if (texture != null)
                {
                    aspectRatioOverride = (texture.width / 2f) / texture.height;
                }
                else
                {
                    aspectRatioOverride = 1.0f;
                }
                manualAspectRatio = false;
                SceneManager.GetActiveScene().UpdateFieldOfView(14.0f);

                // Reset depth parameters to defaults
                depthScale = 2.0f;
                depthBias = 0.1f;
                depthPower = 1.2f;
                RGBDShader.SetUniform("depthScale", depthScale);
                RGBDShader.SetUniform("depthBias", depthBias);
                RGBDShader.SetUniform("depthPower", depthPower);
            }
        }

        // Placeholder for mouse input handling; extend this if interactive manipulation is required.
        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {
            // Intentionally left blank for future customization.
        }

        // Loads an image file as a texture when not in video mode.
        public void LoadTexture(BaseMesh mesh)
        {
            if (isVideo)
                return;
            if (!string.IsNullOrEmpty(pendingFilename))
            {
                Filename = pendingFilename;
                pendingFilename = null;
                texture = null;
            }
            if (File.Exists(Filename) && texture == null)
            {
                Bitmap rgbdBitmap = GenerateRGBDImageFromRGBImage(Filename);
                if (rgbdBitmap != null)
                {
                    texture = CreateTextureFromBitmap(rgbdBitmap);
                }
                if (texture != null && texture.width > 0 && texture.height > 0)
                {
                    float computedAspectRatio = (texture.width / 2f) / texture.height;
                    // If no manual override exists, use the computed aspect ratio.
                    if (!manualAspectRatio)
                    {
                        aspectRatioOverride = computedAspectRatio;
                    }
                    mesh.Transform.Scale = new Vector3(aspectRatioOverride, 1, 1);
                }
            }
        }

        // Disposes active video or camera streams and recording writers.
        private void DisposeCurrentStream()
        {
            if (videoReader != null)
            {
                videoReader.Dispose();
                videoReader = null;
            }
            if (cameraReader != null)
            {
                cameraReader.Dispose();
                cameraReader = null;
            }
            if (videoWriter != null)
            {
                videoWriter.Dispose();
                videoWriter = null;
            }
        }

        // Updates frame processing when using video or camera input.
        private void UpdateVideoFrame(BaseMesh mesh)
        {
            if (!string.IsNullOrEmpty(pendingFilename))
            {
                Filename = pendingFilename;
                pendingFilename = null;
                texture = null;
                DisposeCurrentStream();
                videoFinished = false;
                usingCamera = false;
            }
            if (!videoFinished && !usingCamera && videoReader == null)
            {
                videoReader = new AsyncVideoReader(Filename, false, true);
            }
            if (usingCamera)
                cameraReader.PopFrame();
            else
                videoReader.PopFrame();

            IntPtr framePtr;
            int width, height;
            if (usingCamera)
            {
                framePtr = cameraReader.GetCurrentFramePtr();
                width = cameraReader.Width;
                height = cameraReader.Height;
            }
            else
            {
                framePtr = videoReader.GetCurrentFramePtr();
                width = videoReader.Width;
                height = videoReader.Height;
            }

            GPUImage rgbdFrame = GenerateRGBDImageFromRGBBitmap(width, height, framePtr);

            if (texture != null)
                texture.Dispose();
            texture = new Texture("RGBD", rgbdFrame.data, rgbdFrame.width, rgbdFrame.height, !usingCamera);

            if (videoWriter != null)
                videoWriter.WriteFrame(rgbdFrame.data);

            if (videoWriter != null && videoReader.HasLooped)
            {
                videoWriter.Dispose();
                videoWriter = null;
                videoReader.Dispose();
                videoReader = new AsyncVideoReader(Filename, false, true);
            }
        }

        // The main update loop called each frame.
        public void Update(BaseMesh mesh, float deltaTime)
        {
            if (isVideo || usingCamera)
            {
                UpdateVideoFrame(mesh);
            }
            else
            {
                LoadTexture(mesh);
            }
            mesh.Texture = texture;
            mesh.shader = RGBDShader;
            mesh.shader.SetUniform("mode", mode);

            // Update the mesh scale using the aspect ratio override.
            if (texture != null)
            {
                // If no manual aspect ratio is set, update it from the texture.
                if (!manualAspectRatio)
                {
                    aspectRatioOverride = (texture.width / 2f) / texture.height;
                }
                mesh.Transform.Scale = new Vector3(aspectRatioOverride, 1, 1);
            }
        }
    }
}
