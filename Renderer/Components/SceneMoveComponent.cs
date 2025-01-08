using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Scenes;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;

namespace NullEngine.Renderer.Components
{
    public class SceneMoveComponent : IComponent
    {
        public float MovementSpeed = 5.0f; // Speed for WASD movement
        public float RotationSensitivity = 0.1f; // Mouse rotation sensitivity

        private Vector3 sceneRotation; 
        private Vector3 scenePosition;
        private bool initialized;

        public SceneMoveComponent()
        {
            sceneRotation = Vector3.Zero;
            scenePosition = Vector3.Zero;
            initialized = false;
        }

        public void Update(BaseMesh mesh, float deltaTime)
        {
            var activeScene = SceneManager.GetActiveScene();
            if (activeScene == null)
            {
                return; // No active scene to manipulate
            }

            // Initialize the position and rotation if not already done
            if (!initialized)
            {
                scenePosition = activeScene.transform.Position;
                sceneRotation = activeScene.transform.Rotation;
                initialized = true;
            }

            // Update the active scene's transform
            activeScene.transform = new Transform(
                scenePosition,
                sceneRotation,
                activeScene.transform.Scale
            );
        }

        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {
            if (isPressed)
            {
                // Adjust rotation based on mouse movement
                sceneRotation.Y += delta.X * RotationSensitivity; // Rotate around Y-axis (horizontal movement)
                sceneRotation.X -= delta.Y * RotationSensitivity; // Rotate around X-axis (vertical movement)
            }
        }

        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {
            var activeScene = SceneManager.GetActiveScene();

            if (activeScene == null)
            {
                return; // No active scene to manipulate
            }

            // Forward and backward movement (W/S)
            if (keyboardState.IsKeyDown(Keys.W))
            {
                scenePosition += activeScene.Forward * MovementSpeed * deltaTime;
            }
            if (keyboardState.IsKeyDown(Keys.S))
            {
                scenePosition -= activeScene.Forward * MovementSpeed * deltaTime;
            }

            // Left and right strafing (A/D)
            if (keyboardState.IsKeyDown(Keys.A))
            {
                scenePosition -= activeScene.Right * MovementSpeed * deltaTime;
            }
            if (keyboardState.IsKeyDown(Keys.D))
            {
                scenePosition += activeScene.Right * MovementSpeed * deltaTime;
            }

            // Up and down movement (Space/LeftShift)
            if (keyboardState.IsKeyDown(Keys.Space))
            {
                scenePosition += activeScene.Up * MovementSpeed * deltaTime;
            }
            if (keyboardState.IsKeyDown(Keys.LeftShift))
            {
                scenePosition -= activeScene.Up * MovementSpeed * deltaTime;
            }
        }

        public object Clone()
        {
            return new SceneMoveComponent
            {
                MovementSpeed = this.MovementSpeed,
                RotationSensitivity = this.RotationSensitivity,
                scenePosition = this.scenePosition,
                sceneRotation = this.sceneRotation,
                initialized = this.initialized
            };
        }
    }
}
