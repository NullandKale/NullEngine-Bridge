using NullEngine.Renderer.Components;
using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Shaders;
using NullEngine.Renderer.Textures;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RGBDRenderer.Components
{
    // RGBDComponent implements NullEngine.Renderer.Components.IComponent
    // and is responsible for using a specially crafted shader to displace mesh vertices
    // based on the depth data encoded into the texture (the "right half" of the image).
    public class RGBDComponent : IComponent
    {
        // This Filename is filled from the JSON file’s "Properties" section
        public string Filename;

        // To store the loaded texture.
        public Texture texture = null;

        // The custom shader that does the depth-based displacement
        public Shader RGBDShader;

        // Track pending drag-and-drop files
        private string pendingFilename;

        public RGBDComponent() 
        {
            Filename = "";
            texture = null;

            Program.window.FileDrop += FileDrop;

            // A basic vertex shader that:
            // 1) Extracts depth from the right side (x from 0.5 to 1.0) of the provided texture
            // 2) Inverts that depth
            // 3) Offsets the mesh vertices by that depth times the local normal
            // The fragment shader then samples color from the left side (x from 0.0 to 0.5).
            RGBDShader = new Shader(
                """
                #version 330 core

                // this is the standard mesh layout, we have a position, normal and uv for each vertex
                layout (location = 0) in vec3 position;
                layout (location = 1) in vec3 normal;
                layout (location = 2) in vec2 texCoords;

                // this vert shader returns just the texture coords
                out vec2 fragTexCoords;

                // these are all from the camera system, the model belongs to the mesh, and the view and projection are a combo of the 
                // scenes transform object and the lkg camera
                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;

                // this is the texture, its an RGB+Depth image. The left side of the texture is the color, and the right side is the depth white is close and black is far
                uniform sampler2D textureSampler;

                void main()
                {
                    // Sample the right half of the texture for depth
                    // 'textureSampler' has the entire image, so we shift texCoords.x by +0.5 
                    // to read from the right half. Then invert it (1.0 - that value).
                    float depthVal = 1.0 - texture(textureSampler, vec2(texCoords.x * 0.5 + 0.5, texCoords.y)).r;

                    // Shift from [0..1] to [-0.5..0.5]
                    depthVal -= 0.5;

                    // Offset the vertex in local space along its local normal
                    vec3 displacedPos = position + normal * depthVal;

                    // Standard model/view/projection transform to get to screen space
                    gl_Position = projection * view * model * vec4(displacedPos, 1.0);

                    // For the color, we use the left half (so texCoords.x * 0.5)
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
                    // Simply sample the left half for the color portion
                    FragColor = texture(textureSampler, fragTexCoords);
                }
                """);
        }

        private void FileDrop(OpenTK.Windowing.Common.FileDropEventArgs obj)
        {

            if (obj.FileNames.Length > 0 && File.Exists(obj.FileNames[0]))
            {
                    // Store the first valid file for processing in the next update
                    pendingFilename = obj.FileNames[0];
            }
        }

        // Because of how the JSON scene system works, we need to be able to "clone"
        // our component so it can be reused without reinitializing everything from scratch
        public object Clone()
        {
            RGBDComponent clone = new RGBDComponent();
            clone.Filename = Filename;
            return clone;
        }

        // Keyboard input callback (once per frame). 
        // We can read the keyboard state and / or manipulate the mesh if needed.
        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {

        }

        // Mouse input callback (once per frame). 
        // We can read mouse states and / or manipulate the mesh if needed.
        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {

        }

        // LoadTexture is a helper to load the texture from disk, if it hasn't been loaded yet
        public void LoadTexture(BaseMesh mesh)
        {
            // Process pending drag-and-drop file
            if (!string.IsNullOrEmpty(pendingFilename))
            {
                Filename = pendingFilename; // Update filename
                pendingFilename = null;     // Clear pending state
                texture = null;             // Force texture reload
            }

            if (File.Exists(Filename) && texture == null)
            {
                TextureManager.LoadTexture("RGBD", Filename, false);
                texture = TextureManager.GetTexture("RGBD");

                if (texture != null && texture.width > 0 && texture.height > 0)
                {
                    // Calculate aspect ratio of the COLOR portion (left half of image)
                    float aspectRatio = (texture.width / 2f) / texture.height;

                    // Scale mesh to match texture aspect ratio (assuming original mesh is 1x1 unit)
                    // We preserve the Y scale at 1 and adjust X scale to match aspect ratio
                    mesh.Transform.Scale = new Vector3(aspectRatio, 1, 1);
                }
                else
                {
                    // Fallback to default scale if texture loading failed
                    mesh.Transform.Scale = Vector3.One;
                }
            }
        }

        // Called once per frame to update the component
        public void Update(BaseMesh mesh, float deltaTime)
        {
            // Load the texture once if not loaded yet
            LoadTexture(mesh);

            // Attach our shader and texture to the mesh, so the render pipeline uses them
            mesh.Texture = texture;
            mesh.shader = RGBDShader;

            // Calculate aspect ratio of the COLOR portion (left half of image)
            float aspectRatio = (texture.width / 2f) / texture.height;

            // Scale mesh to match texture aspect ratio (assuming original mesh is 1x1 unit)
            // We preserve the Y scale at 1 and adjust X scale to match aspect ratio
            mesh.Transform.Scale = new Vector3(aspectRatio, 1, 1);
        }
    }
}
