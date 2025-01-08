using System;
using System.Diagnostics;
using OpenTK.Mathematics; // For Vector3 and Matrix4

namespace NullEngine
{
      public class LKGCamera
    {
        // Public variables for camera parameters
        public float Size;           // Half-height of focal plane
        public Vector3 Center;       // Camera target (center)
        public Vector3 Up;           // Up vector
        public float Fov;            // Field of view in degrees
        public float Viewcone;       // Degrees from leftmost view to rightmost view, should be determined by display
        public float AspectRatio;    // Aspect ratio of the viewport
        public float NearPlane;      // Near clipping plane
        public float FarPlane;       // Far clipping plane

        // Default constructor
        public LKGCamera()
        {
            Size = 10.0f;
            Center = new Vector3(0.0f, 0.0f, 0.0f);
            Up = new Vector3(0.0f, 1.0f, 0.0f);
            Fov = 45.0f;
            Viewcone = 40.0f;
            AspectRatio = 1.0f;
            NearPlane = 0.1f;
            FarPlane = 100.0f;
        }

        // Parameterized constructor
        public LKGCamera(float size, Vector3 center, Vector3 upVec,
            float fieldOfView, float viewcone, float aspect, float nearP, float farP)
        {
            Size = size;
            Center = center;
            Up = upVec;
            Fov = fieldOfView;
            Viewcone = viewcone;
            AspectRatio = aspect;
            NearPlane = nearP;
            FarPlane = farP;
        }

        // Get the camera's distance from center of focal plane, given FOV
        public float GetCameraDistance()
        {
            return Size / MathF.Tan(Fov * (MathF.PI / 180.0f));
        }

        // Get the camera's offset based on the viewcone
        public float GetCameraOffset()
        {
            return GetCameraDistance() * MathF.Tan(Viewcone * (MathF.PI / 180.0f));
        }

        // Compute view and projection matrices for hologram views
        public void ComputeViewProjectionMatrices(float normalizedView, bool invert, float depthiness, float focus, out Matrix4 viewMatrix, out Matrix4 projectionMatrix)
        {
            // Adjust camera position based on normalizedView and depthiness
            float offset = -(normalizedView - 0.5f) * depthiness * GetCameraOffset();
            Vector3 adjustedPosition = Center + new Vector3(offset, 0.0f, 0.0f);

            // Adjust up vector if invert is true
            Vector3 adjustedUp = invert ? new Vector3(Up.X, -Up.Y, Up.Z) : Up;

            // Compute the view matrix with the adjusted position and up vector
            viewMatrix = ComputeViewMatrix(Size, Center, adjustedUp, offset);

            // Compute the standard projection matrix
            projectionMatrix = ComputeProjectionMatrix();

            // Apply frustum shift to the projection matrix
            float viewPosition = normalizedView;
            float centerPosition = 0.5f;
            float distanceFromCenter = viewPosition - centerPosition;
            float frustumShift = distanceFromCenter * focus;

            // Modify the projection matrix to include frustum shift (column-major order)
            projectionMatrix.M31 += (offset * 2.0f / (Size * AspectRatio)) + frustumShift;
        }

        // Helper method to compute the view matrix
        private Matrix4 ComputeViewMatrix(float size, Vector3 center, Vector3 upVec, float offset)
        {
            // Compute forward vector f = normalize(center - eye)
            Vector3 f = new Vector3(0.0f, 0.0f, 1.0f); // TODO: Implement camera rotations

            // Compute up vector u = normalize(up)
            Vector3 u = upVec.Normalized();

            // Compute s = normalize(cross(f, u))
            Vector3 s = Vector3.Cross(f, u).Normalized();

            // Recompute up vector u = cross(s, f)
            u = Vector3.Cross(s, f);

            // Build the view matrix in column-major order
            Matrix4 matrix = Matrix4.Identity;

            // Set rotation part
            matrix.M11 = s.X;
            matrix.M12 = u.X;
            matrix.M13 = -f.X;
            matrix.M14 = 0.0f;

            matrix.M21 = s.Y;
            matrix.M22 = u.Y;
            matrix.M23 = -f.Y;
            matrix.M24 = 0.0f;

            matrix.M31 = s.Z;
            matrix.M32 = u.Z;
            matrix.M33 = -f.Z;
            matrix.M34 = 0.0f;

            // Set translation part
            matrix.M41 = offset;
            matrix.M42 = 0.0f;
            matrix.M43 = -GetCameraDistance();
            matrix.M44 = 1.0f;

            return matrix;
        }

        // Helper method to compute the projection matrix
        private Matrix4 ComputeProjectionMatrix()
        {
            float fovRad = Fov * (MathF.PI / 180.0f);
            float f = 1.0f / MathF.Tan(fovRad / 2.0f);
            float aspect = AspectRatio;
            float n = NearPlane;
            float f_p = FarPlane;

            Matrix4 matrix = new Matrix4();

            matrix.M11 = f / aspect;
            matrix.M12 = 0.0f;
            matrix.M13 = 0.0f;
            matrix.M14 = 0.0f;

            matrix.M21 = 0.0f;
            matrix.M22 = f;
            matrix.M23 = 0.0f;
            matrix.M24 = 0.0f;

            matrix.M31 = 0.0f;
            matrix.M32 = 0.0f;
            matrix.M33 = (f_p + n) / (n - f_p);
            matrix.M34 = -1.0f;

            matrix.M41 = 0.0f;
            matrix.M42 = 0.0f;
            matrix.M43 = (2 * f_p * n) / (n - f_p);
            matrix.M44 = 0.0f;

            return matrix;
        }


        // Helper method to convert a Matrix4 to a string for easy debugging
        private string MatrixToString(Matrix4 matrix)
        {
            return $"[{matrix.M11}, {matrix.M12}, {matrix.M13}, {matrix.M14}]\n" +
                   $"[{matrix.M21}, {matrix.M22}, {matrix.M23}, {matrix.M24}]\n" +
                   $"[{matrix.M31}, {matrix.M32}, {matrix.M33}, {matrix.M34}]\n" +
                   $"[{matrix.M41}, {matrix.M42}, {matrix.M43}, {matrix.M44}]\n";
        }

        // Helper method to compute the model matrix (for object transformations)
        public Matrix4 ComputeModelMatrix(float angleX, float angleY)
        {
            float cosX = MathF.Cos(angleX);
            float sinX = MathF.Sin(angleX);
            float cosY = MathF.Cos(angleY);
            float sinY = MathF.Sin(angleY);

            // Rotation around X-axis
            Matrix4 rotationX = Matrix4.Identity;
            rotationX.M22 = cosX;
            rotationX.M23 = -sinX;
            rotationX.M32 = sinX;
            rotationX.M33 = cosX;

            // Rotation around Y-axis
            Matrix4 rotationY = Matrix4.Identity;
            rotationY.M11 = cosY;
            rotationY.M13 = sinY;
            rotationY.M31 = -sinY;
            rotationY.M33 = cosY;

            // Combine rotations
            Matrix4 rotation = rotationY * rotationX;

            // Translation matrix (moving the object back by 3 units on Z-axis)
            Matrix4 translation = Matrix4.Identity;
            translation.M43 = -3.0f;

            // Final model matrix
            Matrix4 modelMatrix = rotation * translation;

            return modelMatrix;
        }
    }
}