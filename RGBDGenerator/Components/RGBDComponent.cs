using GPU;
using ILGPU.Runtime.Cuda;
using LKG_NVIDIA_RAYS.Utils;
using NullEngine.Renderer.Components;
using NullEngine.Renderer.Mesh;
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
    // RGBDComponent handles images, video files, and live camera feeds.
    // For videos, it uses an AsyncVideoReader; for camera feeds, it uses an AsyncCameraReader.
    // In either case, each frame is converted to an RGBD image, saved via VideoWriter, and applied to the mesh.
    public class RGBDComponent : IComponent
    {
        // For image mode the filename is loaded from the JSON "Properties" section.
        public string Filename;
        // The texture that will be used by the mesh.
        public Texture texture = null;
        // The custom shader that does the depth-based displacement.
        public Shader RGBDShader;
        // Pending file from drag-and-drop.
        private string pendingFilename;
        // DepthGenerator used for computing depth.
        private DepthGenerator depthGenerator;

        // --- Fields for video/camera support ---
        // Flag indicating whether the current stream is a video file.
        private bool isVideo = false;
        // Flag indicating that a camera feed is in use.
        private bool usingCamera = false;
        // Flag indicating that the stream has finished (for video files).
        private bool videoFinished = false;
        // The file-based video reader.
        private AsyncVideoReader videoReader = null;
        // The live camera reader.
        private AsyncCameraReader cameraReader = null;
        // VideoWriter used for writing processed frames.
        private VideoWriter videoWriter = null;
        // A temporary GPUImage instance for video frame processing.
        private GPUImage tempVideoFrame;

        public RGBDComponent()
        {
            Filename = "";
            texture = null;

            // Initialize the depth generator with the desired inference size.
            int inferenceSize = 37 * 14;
            depthGenerator = new DepthGenerator(inferenceSize, inferenceSize, "Assets/depth-anything-v2-small.onnx");

            // Subscribe to file drop events.
            Program.window.FileDrop += FileDrop;

            // Create the custom RGBD shader.
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
                void main()
                {
                    float depthVal = 1.0 - texture(textureSampler, vec2(texCoords.x * 0.5 + 0.5, texCoords.y)).r;
                    depthVal -= 0.5;
                    vec3 displacedPos = position + normal * depthVal;
                    gl_Position = projection * view * model * vec4(displacedPos, 1.0);
                    fragTexCoords = vec2(texCoords.x * 0.5, texCoords.y);
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
                ");
        }

        // File drop handler. If a file is dropped, it is assumed to be a video file.
        private void FileDrop(OpenTK.Windowing.Common.FileDropEventArgs obj)
        {
            if (obj.FileNames.Length > 0 && File.Exists(obj.FileNames[0]))
            {
                pendingFilename = obj.FileNames[0];
                string ext = Path.GetExtension(pendingFilename).ToLower();
                isVideo = (ext == ".mp4" || ext == ".avi" || ext == ".mov");
                // When a new file is dropped, we assume it's not a camera feed.
                usingCamera = false;
                videoFinished = false;
            }
        }

        // Generates an RGBD image from an image file.
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

        // Generates an RGBD image from an RGB bitmap (used for video/camera frames).
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

        // Creates a Texture from a Bitmap.
        private Texture CreateTextureFromBitmap(Bitmap bmp)
        {
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            System.Drawing.Imaging.BitmapData bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
            Texture tex = new Texture("RGBD", bmpData.Scan0, bmp.Width, bmp.Height, false);
            bmp.UnlockBits(bmpData);
            return tex;
        }

        // Clones the component.
        public object Clone()
        {
            RGBDComponent clone = new RGBDComponent();
            clone.Filename = Filename;
            return clone;
        }

        // Handles keyboard input. Pressing D0 or D1 switches to a live camera feed.
        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {
            if (keyboardState.IsKeyPressed(Keys.D0))
            {
                // Switch to camera feed with index 0.
                DisposeCurrentStream();
                cameraReader = new AsyncCameraReader(0, true, true);
                usingCamera = true;
                videoFinished = false;
            }
            else if (keyboardState.IsKeyPressed(Keys.D1))
            {
                // Switch to camera feed with index 1.
                DisposeCurrentStream();
                cameraReader = new AsyncCameraReader(1, true, true);
                usingCamera = true;
                videoFinished = false;
            }
        }

        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {
            // Optionally handle mouse input.
        }

        // Loads texture in image mode.
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
                    float aspectRatio = (texture.width / 2f) / texture.height;
                    mesh.Transform.Scale = new Vector3(aspectRatio, 1, 1);
                }
            }
        }

        // Disposes any existing video or camera stream and writer.
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

        // Updates the component in video or camera mode.
        private void UpdateVideoFrame(BaseMesh mesh)
        {
            // If a new file is dropped, reset streams.
            if (!string.IsNullOrEmpty(pendingFilename))
            {
                Filename = pendingFilename;
                pendingFilename = null;
                texture = null;
                DisposeCurrentStream();
                videoFinished = false;
                usingCamera = false;
            }

            // Initialize file-based video stream if needed.
            if (!videoFinished && !usingCamera && videoReader == null)
            {
                videoReader = new AsyncVideoReader(Filename, true, true);
                string outputFilename = "output_" + Path.GetFileName(Filename);
                videoWriter = new VideoWriter(outputFilename, videoReader.Fps, videoReader.Width * 2, videoReader.Height);
            }
            // For camera mode, we assume the cameraReader was already initialized in HandleKeyboardInput.

            // Check end-of-stream for file-based video.
            if (!usingCamera && videoReader != null && videoReader.EndOfVideo)
            {
                DisposeCurrentStream();
                videoFinished = true;
                return;
            }

            // If no stream exists, return.
            if ((!usingCamera && videoReader == null) || (usingCamera && cameraReader == null))
                return;

            // Advance the frame.
            if (usingCamera)
                cameraReader.PopFrame();
            else
                videoReader.PopFrame();

            // Retrieve the frame data.
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

            // Process the frame to produce an RGBD image.
            GPUImage rgbdFrame = GenerateRGBDImageFromRGBBitmap(width, height, framePtr);

            // Update texture.
            if (texture != null)
            {
                texture.Dispose();
            }
            texture = new Texture("RGBD", rgbdFrame.data, rgbdFrame.width, rgbdFrame.height, !usingCamera);

            // Write the processed frame to the output video.
            if(videoWriter != null)
            {
                videoWriter.WriteFrame(rgbdFrame.data);
            }
        }

        // Update method called every frame.
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
            if (texture != null)
            {
                float aspectRatio = (texture.width / 2f) / texture.height;
                mesh.Transform.Scale = new Vector3(aspectRatio, 1, 1);
            }
        }
    }
}
