using System;
using System.Collections.Generic;
using NullEngine.Renderer.Components;
using NullEngine.Renderer.Shaders;
using NullEngine.Renderer.Textures;
using OpenTK.Graphics.OpenGL;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace NullEngine.Renderer.Mesh
{
    public class BaseMesh : IDisposable
    {
        private int vao;
        private int vbo;
        private int ebo;

        private float[] vertices;
        private uint[] indices;

        public string name;
        public Transform Transform;
        public Texture Texture;
        public Shader shader;

        // Components list
        private List<IComponent> components = new List<IComponent>();

        public BaseMesh(BaseMesh other)
        {
            // Deep copy the transform
            Transform = new Transform(other.Transform.Position, other.Transform.Rotation, other.Transform.Scale);

            // Deep copy components
            components = new List<IComponent>();
            foreach (var component in other.components)
            {
                var clonedComponent = component.Clone() as IComponent;
                if (clonedComponent != null)
                {
                    components.Add(clonedComponent);
                }
                else
                {
                    throw new InvalidOperationException($"Component {component.GetType().Name} does not support cloning.");
                }
            }

            // Deep copy vertex and index data
            vertices = (float[])other.vertices.Clone();
            indices = (uint[])other.indices.Clone();

            // Share the shader and texture (assuming texture sharing is desired)
            shader = other.shader;
            Texture = other.Texture;
            name = other.name;

            InitializeMesh();
        }

        public BaseMesh(string name, float[] vertices, uint[] indices, Texture texture)
        {
            this.name = name;
            this.vertices = vertices;
            this.indices = indices;
            Texture = texture;
            shader = ShaderManager.GetShader("basic");
            Transform = new Transform(Vector3.Zero, Vector3.Zero, Vector3.One);
            InitializeMesh();
        }

        private void InitializeMesh()
        {
            vao = GL.GenVertexArray();
            vbo = GL.GenBuffer();
            ebo = GL.GenBuffer();

            GL.BindVertexArray(vao);

            // Upload vertex data
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            // Upload index data
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, ebo);
            GL.BufferData(BufferTarget.ElementArrayBuffer, indices.Length * sizeof(uint), indices, BufferUsageHint.StaticDraw);

            // Set vertex attribute pointers
            // Position (location = 0)
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 0);
            GL.EnableVertexAttribArray(0);

            // Normal (location = 1)
            GL.VertexAttribPointer(1, 3, VertexAttribPointerType.Float, false, 8 * sizeof(float), 3 * sizeof(float));
            GL.EnableVertexAttribArray(1);

            // Texture coordinates (location = 2)
            GL.VertexAttribPointer(2, 2, VertexAttribPointerType.Float, false, 8 * sizeof(float), 6 * sizeof(float));
            GL.EnableVertexAttribArray(2);

            GL.BindVertexArray(0);
        }

        public virtual void HandleMouseInput(MouseState mouseState, Vector2 delta, bool isPressed)
        {
            foreach (var component in components)
            {
                component.HandleMouseInput(this, mouseState, delta, isPressed);
            }
        }

        public virtual void HandleKeyboardInput(KeyboardState keyboardState, float deltaTime)
        {
            foreach (var component in components)
            {
                component.HandleKeyboardInput(this, keyboardState, deltaTime);
            }
        }

        public void Draw(Matrix4 viewMatrix, Matrix4 projectionMatrix)
        {
            Texture.Bind(TextureUnit.Texture0);

            shader.SetUniform("model", Transform.GetModelMatrix());
            shader.SetUniform("view", viewMatrix);
            shader.SetUniform("projection", projectionMatrix);
            shader.SetUniform("textureSampler", 0); 

            shader.Use();

            GL.BindVertexArray(vao);
            GL.DrawElements(PrimitiveType.Triangles, indices.Length, DrawElementsType.UnsignedInt, 0);
        }

        public void UpdateVertices(float[] newVertices)
        {
            vertices = newVertices;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
        }

        public virtual void Update(float deltaTime)
        {
            foreach (var component in components)
            {
                component.Update(this, deltaTime);
            }
        }

        // Methods to manage components
        public void AddComponent(IComponent component)
        {
            if (component != null && !components.Contains(component))
            {
                components.Add(component);
            }
        }

        public void RemoveComponent(IComponent component)
        {
            if (component != null && components.Contains(component))
            {
                components.Remove(component);
            }
        }

        public void Dispose()
        {
            GL.DeleteVertexArray(vao);
            GL.DeleteBuffer(vbo);
            GL.DeleteBuffer(ebo);
            Texture.Dispose();
        }

        public List<T> GetComponents<T>() where T : IComponent
        {
            List<T> result = new List<T>();

            foreach (var component in components)
            {
                if (component is T typedComponent)
                {
                    result.Add(typedComponent);
                }
            }

            return result;
        }

    }
}
