using GPU;
using ILGPU.Runtime.Cuda;
using LKG_NVIDIA_RAYS.Utils;
using NullEngine.Renderer.Components;
using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Shaders;
using NullEngine.Renderer.Textures;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static OpenCvSharp.Stitcher;
using static System.Net.Mime.MediaTypeNames;

namespace RGBDGenerator.Components
{
    // RGBDComponent implements IComponent and is responsible for using a specially crafted shader 
    // to displace mesh vertices based on the depth data encoded into the texture.
    // If a new input image is provided (via drag-and-drop), it is assumed not to be an RGBD image.
    // In that case, the component generates an RGBD image from the input using the DepthGenerator.
    public class RGBDComponent : IComponent
    {
        // This Filename is filled from the JSON file’s "Properties" section
        public string Filename;

        // To store the loaded texture.
        public Texture texture = null;

        // The custom shader that does the depth-based displacement.
        public Shader RGBDShader;

        // Track pending drag-and-drop files.
        private string pendingFilename;

        // DepthGenerator instance to compute depth for non-RGBD input images.
        private DepthGenerator depthGenerator;

        public RGBDComponent()
        {
            Filename = "";
            texture = null;

            // Initialize the depth generator with the desired inference size.
            int inferenceSize = 42 * 14; 
            depthGenerator = new DepthGenerator(inferenceSize, inferenceSize, "Assets/depth-anything-v2-small.onnx");

            // Subscribe to file drop events.
            Program.window.FileDrop += FileDrop;

            // Create the custom RGBD shader.
            RGBDShader = new Shader(
                // Vertex shader
                """
                #version 330 core

                // Standard mesh layout: position, normal, and texture coordinates.
                layout (location = 0) in vec3 position;
                layout (location = 1) in vec3 normal;
                layout (location = 2) in vec2 texCoords;

                // Pass texture coordinates to the fragment shader.
                out vec2 fragTexCoords;

                // Uniforms for transformation matrices.
                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;

                // The RGBD texture: left half is color, right half is depth.
                uniform sampler2D textureSampler;

                void main()
                {
                    // Sample the right half for depth.
                    float depthVal = 1.0 - texture(textureSampler, vec2(texCoords.x * 0.5 + 0.5, texCoords.y)).r;
                    // Shift depth from [0,1] to [-0.5,0.5].
                    depthVal -= 0.5;
                    // Displace the vertex along its normal by the depth value.
                    vec3 displacedPos = position + normal * depthVal;

                    // Standard model/view/projection transformation.
                    gl_Position = projection * view * model * vec4(displacedPos, 1.0);

                    // For color sampling, use the left half of the texture.
                    fragTexCoords = vec2(texCoords.x * 0.5, texCoords.y);
                }
                """,
                // Fragment shader
                """
                #version 330 core

                in vec2 fragTexCoords;
                out vec4 FragColor;
                uniform sampler2D textureSampler;

                void main()
                {
                    // Sample the color from the left half.
                    FragColor = texture(textureSampler, fragTexCoords);
                }
                """);
        }

        /// <summary>
        /// Generates an RGBD image from an RGB input image.
        /// Loads the image from disk, computes depth using DepthGenerator,
        /// and returns the resulting RGBD image as a Bitmap.
        /// </summary>
        private Bitmap GenerateRGBDImageFromRGBImage(string filename)
        {
            if (GPUImage.TryLoad(filename, out GPUImage loadedImage))
            {
                GPUImage output = depthGenerator.ComputeDepth(loadedImage);
                loadedImage.Dispose();
                return output.GetBitmap();
            }
            else
            {
                return null;
            }
        }

        /// <summary>
        /// Handler for file drop events. Stores the first valid file for processing.
        /// </summary>
        private void FileDrop(OpenTK.Windowing.Common.FileDropEventArgs obj)
        {
            if (obj.FileNames.Length > 0 && File.Exists(obj.FileNames[0]))
            {
                pendingFilename = obj.FileNames[0];
            }
        }

        // Because of how the JSON scene system works, we need to be able to "clone"
        // our component so it can be reused without reinitializing everything from scratch.
        public object Clone()
        {
            RGBDComponent clone = new RGBDComponent();
            clone.Filename = Filename;
            return clone;
        }

        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {
            // Optionally handle keyboard input.
        }

        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {
            // Optionally handle mouse input.
        }

        /// <summary>
        /// Loads or updates the texture for the mesh.
        /// If a new file has been provided (via drag-and-drop), it is processed to generate an RGBD image.
        /// The resulting RGBD image is then loaded into the TextureManager.
        /// </summary>
        public void LoadTexture(BaseMesh mesh)
        {
            // Process any pending drag-and-drop file.
            if (!string.IsNullOrEmpty(pendingFilename))
            {
                Filename = pendingFilename;
                pendingFilename = null;
                texture = null;
            }

            // Only attempt to load a new texture if we don't already have one.
            if (File.Exists(Filename) && texture == null)
            {
                // Assume new input images are not already RGBD.
                // Generate an RGBD image from the input.
                Bitmap rgbdBitmap = GenerateRGBDImageFromRGBImage(Filename);
                if (rgbdBitmap != null)
                {
                    // Use the pointer-based constructor to create the texture.
                    Rectangle rect = new Rectangle(0, 0, rgbdBitmap.Width, rgbdBitmap.Height);
                    System.Drawing.Imaging.BitmapData bmpData = rgbdBitmap.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, rgbdBitmap.PixelFormat);

                    texture = new Texture("RGBD", bmpData.Scan0, rgbdBitmap.Width, rgbdBitmap.Height, false);

                    rgbdBitmap.UnlockBits(bmpData);
                }

                // Only update the mesh if we have a valid texture.
                if (texture != null && texture.width > 0 && texture.height > 0)
                {
                    float aspectRatio = (texture.width / 2f) / texture.height;
                    mesh.Transform.Scale = new Vector3(aspectRatio, 1, 1);
                }
            }
        }

        /// <summary>
        /// Called once per frame to update the component.
        /// Loads the texture if needed, and attaches the shader and texture to the mesh.
        /// </summary>
        public void Update(BaseMesh mesh, float deltaTime)
        {
            LoadTexture(mesh);
            mesh.Texture = texture;
            mesh.shader = RGBDShader;

            // Update the mesh scale to match the aspect ratio of the COLOR portion.
            if (texture != null)
            {
                float aspectRatio = (texture.width / 2f) / texture.height;
                mesh.Transform.Scale = new Vector3(aspectRatio, 1, 1);
            }
        }
    }
}
