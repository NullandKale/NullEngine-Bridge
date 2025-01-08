using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;

namespace NullEngine.Renderer.Shaders
{
    public class Shader
    {
        private int programId;
        private Dictionary<string, Action> uniformCache = new Dictionary<string, Action>();

        public Shader(string vertexSource, string fragmentSource)
        {
            // Create and compile the vertex shader
            int vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, vertexSource);
            GL.CompileShader(vertexShader);
            CheckShaderCompileStatus(vertexShader, "Vertex");

            // Create and compile the fragment shader
            int fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, fragmentSource);
            GL.CompileShader(fragmentShader);
            CheckShaderCompileStatus(fragmentShader, "Fragment");

            // Create and link the shader program
            programId = GL.CreateProgram();
            GL.AttachShader(programId, vertexShader);
            GL.AttachShader(programId, fragmentShader);
            GL.LinkProgram(programId);
            CheckProgramLinkStatus();

            // Cleanup individual shaders
            GL.DeleteShader(vertexShader);
            GL.DeleteShader(fragmentShader);
        }

        public void Use()
        {
            GL.UseProgram(programId);
            ApplyUniforms();
        }

        public int GetUniformLocation(string name)
        {
            return GL.GetUniformLocation(programId, name);
        }

        // Uniform setters with caching
        public void SetUniform(string name, int value)
        {
            uniformCache[name] = () =>
            {
                int location = GetUniformLocation(name);
                if (location != -1)
                {
                    GL.Uniform1(location, value);
                }
            };
        }

        public void SetUniform(string name, float value)
        {
            uniformCache[name] = () =>
            {
                int location = GetUniformLocation(name);
                if (location != -1)
                {
                    GL.Uniform1(location, value);
                }
            };
        }

        public void SetUniform(string name, Vector3 value)
        {
            uniformCache[name] = () =>
            {
                int location = GetUniformLocation(name);
                if (location != -1)
                {
                    GL.Uniform3(location, value);
                }
            };
        }

        public void SetUniform(string name, Vector4 value)
        {
            uniformCache[name] = () =>
            {
                int location = GetUniformLocation(name);
                if (location != -1)
                {
                    GL.Uniform4(location, value);
                }
            };
        }

        public void SetUniform(string name, Matrix4 value)
        {
            uniformCache[name] = () =>
            {
                int location = GetUniformLocation(name);
                if (location != -1)
                {
                    GL.UniformMatrix4(location, false, ref value);
                }
            };
        }

        // Apply all cached uniforms
        private void ApplyUniforms()
        {
            foreach (var uniform in uniformCache.Values)
            {
                uniform.Invoke();
            }
        }

        public void Dispose()
        {
            GL.DeleteProgram(programId);
        }

        private void CheckShaderCompileStatus(int shader, string type)
        {
            GL.GetShader(shader, ShaderParameter.CompileStatus, out int status);
            if (status == 0)
            {
                string log = GL.GetShaderInfoLog(shader);
                throw new Exception($"Error compiling {type} shader: {log}");
            }
        }

        private void CheckProgramLinkStatus()
        {
            GL.GetProgram(programId, GetProgramParameterName.LinkStatus, out int status);
            if (status == 0)
            {
                string log = GL.GetProgramInfoLog(programId);
                throw new Exception($"Error linking shader program: {log}");
            }
        }
    }
}
