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
    public class RGBDComponent : IComponent
    {
        public string Filename;
        public Texture texture = null;

        public Shader RGBDShader;

        public RGBDComponent() 
        {
            Filename = "";
            texture = null;

            RGBDShader = new Shader(
                """
                #version 330 core

                // this is our mesh layout, we have a position, normal and uv for each vertex
                layout (location = 0) in vec3 position;
                layout (location = 1) in vec3 normal;
                layout (location = 2) in vec2 texCoords;

                // the vert shader returns the texture coords
                out vec2 fragTexCoords;

                // these are all from the camera system, the model belongs to the mesh, and the view and projection are a combo of the 
                // scenes transform object and the lkg camera
                uniform mat4 model;
                uniform mat4 view;
                uniform mat4 projection;

                // this is the texture, its an RGB+Depth image, so the left side of the texture is the color, and the right side is the depth white is close and black is far
                uniform sampler2D textureSampler;

                void main()
                {
                    // 1) Grab the depth from the right half of the texture and invert
                    float depthVal = 1.0 - texture(textureSampler, vec2(texCoords.x * 0.5 + 0.5, texCoords.y)).r;

                    // 2) Remap depth from [0..1] to [-0.5..0.5]
                    depthVal -= 0.5;

                    // 3) Displace the vertex in local space along its local normal
                    vec3 displacedPos = position + normal * depthVal;

                    // 4) Transform by model/view/projection
                    gl_Position = projection * view * model * vec4(displacedPos, 1.0);

                    // Sample the color from the left half of the texture
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
                    // use the fragTexCoords which we already transformed to the left half of the texture
                    FragColor = texture(textureSampler, fragTexCoords);
                }
                """);
        }

        public object Clone()
        {
            RGBDComponent clone = new RGBDComponent();
            clone.Filename = Filename;
            return clone;
        }

        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {

        }

        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {

        }

        public void LoadTexture()
        {
            if (File.Exists(Filename) && texture == null)
            {
                TextureManager.LoadTexture("RGBD", Filename);
                texture = TextureManager.GetTexture("RGBD");
            }
        }

        public void Update(BaseMesh mesh, float deltaTime)
        {
            LoadTexture();
            mesh.Texture = texture;
            mesh.shader = RGBDShader;
        }
    }
}
