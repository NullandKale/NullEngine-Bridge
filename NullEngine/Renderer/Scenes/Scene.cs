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

        public Scene(BridgeWindowData bridgeData, Transform transform, float cameraSize = 1.0f, float focus = 0.0f, float offset = 1.0f)
        {
            CameraSize = cameraSize;
            Focus = focus;
            Offset = offset;

            this.transform = transform;

            Vector3 LKG_target = new Vector3(0.0f, 0.0f, 0.0f);

            Vector3 LKG_up = new Vector3(0.0f, 1.0f, 0.0f);

            // check that bridge initialized and the window initialized
            bool isBridgeInitialized = bridgeData != null && bridgeData.Wnd != 0;

            float fov = 14.0f;
            float viewcone = isBridgeInitialized ? bridgeData.Viewcone : 40.0f;
            float aspect = isBridgeInitialized ? bridgeData.DisplayAspect : 1.0f;
            float nearPlane = 0.1f;
            float farPlane = 100.0f;

            camera = new LKGCamera(CameraSize, LKG_target, LKG_up, fov, viewcone, aspect, nearPlane, farPlane);
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

            // Handle keyboard input
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

            // this doesnt work rotation needs to be solved somehow
            //// Apply the scene's transform to the camera's view matrix
            //Matrix4 sceneTransformMatrix = transform.GetModelMatrix();

            //// Combine the scene transform with the view matrix
            //Matrix4 inverseSceneTransform = sceneTransformMatrix.Inverted();
            //Matrix4 combinedViewMatrix = viewMatrix * inverseSceneTransform;

            // Log all matrices and transformation data
            //Log.Debug("=== START CAMERA DEBUG INFO ===");
            //Log.Debug($"Camera Size: {CameraSize}");
            //Log.Debug($"Focus: {Focus}");
            //Log.Debug($"Offset: {Offset}");
            //Log.Debug($"Scene Position: {transform.Position}");
            //Log.Debug($"Scene Rotation (Euler): {transform.Rotation}");
            //Log.Debug($"Scene Scale: {transform.Scale}");
            //Log.Debug("Scene Transform Matrix:");
            //Log.Debug("\n" + sceneTransformMatrix.ToString());
            //Log.Debug("View Matrix:");
            //Log.Debug("\n" + viewMatrix.ToString());
            //Log.Debug("Combined View Matrix:");
            //Log.Debug("\n" + combinedViewMatrix.ToString());
            //Log.Debug($"Forward Vector: {Forward}");
            //Log.Debug($"Right Vector: {Right}");
            //Log.Debug($"Up Vector: {Up}");
            //Log.Debug("=== END CAMERA DEBUG INFO ===");

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
            foreach(BaseMesh mesh in meshes)
            {
                if(mesh.name == meshName)
                {
                    return mesh;
                }
            }

            return null;
        }
    }
}
