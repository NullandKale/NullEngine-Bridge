using BridgeSDK;
using NullEngine.Renderer.Mesh;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;
using System.Collections.Generic;
using static System.TimeZoneInfo;

namespace NullEngine.Renderer.Scenes
{
    public class Scene
    {
        public Transform transform;
        public Vector3 Forward;
        public Vector3 Right;
        public Vector3 Up;

        public float CameraSize;
        public float Focus;
        public float Offset;
        private List<BaseMesh> meshes = new List<BaseMesh>();
        private LKGCamera camera;

        // Camera parameter fields
        public Vector3 cameraTarget;
        public Vector3 cameraUp;
        public float fov;
        public float viewcone;
        public float aspect;
        public float nearPlane;
        public float farPlane;

        public Scene(BridgeWindowData bridgeData, Transform transform, float cameraSize = 1.0f, float focus = 0.0f, float offset = 1.0f)
        {
            CameraSize = cameraSize;
            Focus = focus;
            Offset = offset;

            this.transform = transform;

            // Check that Bridge is initialized and the window is valid
            bool isBridgeInitialized = bridgeData != null && bridgeData.Wnd != 0;

            // Initialize camera parameters
            cameraTarget = new Vector3(0.0f, 0.0f, 0.0f);
            cameraUp = new Vector3(0.0f, 1.0f, 0.0f);
            fov = 14.0f;
            viewcone = isBridgeInitialized ? bridgeData.Viewcone : 40.0f;
            aspect = isBridgeInitialized ? bridgeData.DisplayAspect : 1.0f;
            nearPlane = 0.1f;
            farPlane = 100.0f;

            // Construct the initial camera
            camera = new LKGCamera(CameraSize, cameraTarget, cameraUp, fov, viewcone, aspect, nearPlane, farPlane);
        }

        private void ReconstructCamera()
        {
            camera = new LKGCamera(CameraSize, cameraTarget, cameraUp, fov, viewcone, aspect, nearPlane, farPlane);
        }

        public void UpdateCameraTarget(Vector3 newTarget)
        {
            cameraTarget = newTarget;
            ReconstructCamera();
        }

        public void UpdateCameraUp(Vector3 newUp)
        {
            cameraUp = newUp;
            ReconstructCamera();
        }

        public void UpdateFieldOfView(float newFov)
        {
            fov = newFov;
            ReconstructCamera();
        }

        public void UpdateViewcone(float newViewcone)
        {
            viewcone = newViewcone;
            ReconstructCamera();
        }

        public void UpdateAspect(float newAspect)
        {
            aspect = newAspect;
            ReconstructCamera();
        }

        public void UpdateNearPlane(float newNearPlane)
        {
            nearPlane = newNearPlane;
            ReconstructCamera();
        }

        public void UpdateFarPlane(float newFarPlane)
        {
            farPlane = newFarPlane;
            ReconstructCamera();
        }

        public void AddMesh(BaseMesh mesh)
        {
            // Ensure that the mesh being added is a deep copy
            BaseMesh copiedMesh = new BaseMesh(mesh);
            meshes.Add(copiedMesh);
        }

        public void RemoveMesh(BaseMesh mesh)
        {
            meshes.Remove(mesh);
        }

        public void HandleMouseInput(MouseState mouseState, Vector2 delta, bool isPressed)
        {
            foreach (var mesh in meshes)
            {
                mesh.HandleMouseInput(mouseState, delta, isPressed);
            }
        }

        public void HandleKeyboardInput(KeyboardState keyboardState, float deltaTime)
        {
            foreach (var mesh in meshes)
            {
                mesh.HandleKeyboardInput(keyboardState, deltaTime);
            }

            // Handle keyboard input for Focus and Offset adjustments
            if (keyboardState.IsKeyDown(Keys.Up))
            {
                Focus += deltaTime;
            }
            if (keyboardState.IsKeyDown(Keys.Down))
            {
                Focus -= deltaTime;
            }
            if (keyboardState.IsKeyDown(Keys.Left))
            {
                Offset -= deltaTime;
            }
            if (keyboardState.IsKeyDown(Keys.Right))
            {
                Offset += deltaTime;
            }

            Focus = MathHelper.Clamp(Focus, -4.0f, 4.0f);
            Offset = MathHelper.Clamp(Offset, 0.0f, 2.0f);
        }

        public void Update(float deltaTime)
        {
            foreach (var mesh in meshes)
            {
                mesh.Update(deltaTime);
            }

            Forward = transform.Forward();
            Right = transform.Right();
            Up = transform.Up();
        }

        public void Render(float normalizedView = 0.5f, bool invert = false)
        {
            // Compute the camera's view and projection matrices
            camera.ComputeViewProjectionMatrices(normalizedView, invert, Offset, Focus, out Matrix4 viewMatrix, out Matrix4 projectionMatrix);

            // Render all meshes
            foreach (var mesh in meshes)
            {
                mesh.Draw(viewMatrix, projectionMatrix);
            }
        }

        public LKGCamera GetCamera()
        {
            return camera;
        }

        public BaseMesh GetMesh(string meshName)
        {
            foreach (BaseMesh mesh in meshes)
            {
                if (mesh.name == meshName)
                {
                    return mesh;
                }
            }
            return null;
        }
    }
}
