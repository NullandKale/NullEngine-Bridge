using NullEngine.Renderer.Mesh;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace NullEngine.Renderer.Components
{
    public class RotateComponent : IComponent
    {
        public float sensitivity = 0.1f; // Controls how sensitive the rotation is to mouse movement
        private Vector3 rotation; // Local rotation stored by this component
        private bool initialized; // Tracks whether the component has been initialized with the mesh's rotation

        public RotateComponent()
        {
            sensitivity = 0.1f;
            rotation = Vector3.Zero;
            initialized = false;
        }

        public RotateComponent(float sensitivity = 0.1f)
        {
            this.sensitivity = sensitivity;
            rotation = Vector3.Zero;
            initialized = false;
        }

        public void Update(BaseMesh mesh, float deltaTime)
        {
            if (!initialized)
            {
                // Initialize with the mesh's current rotation if not already done
                rotation = mesh.Transform.Rotation;
                initialized = true;
            }

            // Update the mesh's Transform rotation based on this component's rotation
            mesh.Transform = new Transform(
                mesh.Transform.Position,
                rotation,
                mesh.Transform.Scale
            );
        }

        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {
            if (isPressed)
            {
                // Adjust rotation based on mouse movement and sensitivity
                rotation.Y -= delta.X * sensitivity; // Rotate around Y-axis (horizontal movement)
                rotation.X -= delta.Y * sensitivity; // Rotate around X-axis (vertical movement)
            }
        }

        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {
            // Not used in this component, but could be extended for keyboard-driven rotation
        }

        public object Clone()
        {
            return new RotateComponent(sensitivity)
            {
                rotation = rotation,
                initialized = initialized
            };
        }
    }
}
