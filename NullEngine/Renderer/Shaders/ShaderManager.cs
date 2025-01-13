using System;
using System.Collections.Generic;

namespace NullEngine.Renderer.Shaders
{
    public static class ShaderManager
    {
        private static Dictionary<string, Shader> shaders = new Dictionary<string, Shader>();

        public static void LoadShaders()
        {
            shaders["basic"] = new Shader(
            @"
            #version 330 core
            layout (location = 0) in vec3 position;
            layout (location = 1) in vec3 normal;
            layout (location = 2) in vec2 texCoords;

            out vec2 fragTexCoords;
            out vec3 fragNormal;

            uniform mat4 model;
            uniform mat4 view;
            uniform mat4 projection;

            void main()
            {
                gl_Position = projection * view * model * vec4(position, 1.0);
                fragTexCoords = texCoords;
                fragNormal = normal;
            }
            ",
            @"
            #version 330 core
            in vec2 fragTexCoords;
            in vec3 fragNormal;
            out vec4 FragColor;

            uniform sampler2D textureSampler;

            void main()
            {
                //FragColor = vec4(abs(fragNormal), 1.0);
                FragColor = texture(textureSampler, fragTexCoords);
            }
            "
            );
        }

        public static Shader GetShader(string name)
        {
            if (shaders.ContainsKey(name))
                return shaders[name];

            Log.Warn($"Shader '{name}' not found.");
            return null;
        }

        public static void Dispose()
        {
            foreach (var shader in shaders.Values)
            {
                shader.Dispose();
            }
            shaders.Clear();
        }

        public static void LoadShader(string name, string vert, string frag)
        {
            shaders[name] = new Shader(vert, frag);
        }
    }
}
