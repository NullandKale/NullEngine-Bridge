using OpenTK.Mathematics;
using System;

namespace NullEngine.Renderer
{
    public struct Transform
    {
        public Vector3 Position;
        public Vector3 Rotation; // Euler angles in degrees: (pitch = X, yaw = Y, roll = Z)
        public Vector3 Scale;

        public Transform(Vector3 position, Vector3 rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public Matrix4 GetModelMatrix()
        {
            Matrix4 model = Matrix4.Identity;
            model *= Matrix4.CreateScale(Scale);
            model *= Matrix4.CreateRotationX(MathHelper.DegreesToRadians(Rotation.X)); // pitch
            model *= Matrix4.CreateRotationY(MathHelper.DegreesToRadians(Rotation.Y)); // yaw
            model *= Matrix4.CreateRotationZ(MathHelper.DegreesToRadians(Rotation.Z)); // roll
            model *= Matrix4.CreateTranslation(Position);
            return model;
        }

        public Vector3 Forward()
        {
            float pitch = MathHelper.DegreesToRadians(Rotation.X);
            float yaw = MathHelper.DegreesToRadians(Rotation.Y);

            float x = MathF.Cos(yaw) * MathF.Cos(pitch);
            float y = MathF.Sin(pitch);
            float z = MathF.Sin(yaw) * MathF.Cos(pitch);

            return new Vector3(x, y, z).Normalized();
        }

        public Vector3 Right()
        {
            return Vector3.Cross(Forward(), new Vector3(0, 1, 0)).Normalized();
        }

        public Vector3 Up()
        {
            return Vector3.Cross(Right(), Forward()).Normalized();
        }
    }
}
