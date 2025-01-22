using System;
using OpenCvSharp;
using OpenTK.Graphics.OpenGL;
using NullEngine.Renderer.Textures;
using System.Drawing;

namespace NullEngine.Renderer.Textures
{
    public static class TextureGenerator
    {
        /// <summary>
        /// Generates a solid color texture.
        /// </summary>
        public static Texture GenerateSolidColor(string name, Color color, int width = 256, int height = 256)
        {
            // Create Mat with BGRA color (OpenCV default format)
            using Mat mat = new Mat(height, width, MatType.CV_8UC4, new Scalar(
                color.B,
                color.G,
                color.R,
                color.A
            ));

            return CreateTextureFromMat(name, mat);
        }

        /// <summary>
        /// Generates a vertical gradient texture.
        /// </summary>
        public static Texture GenerateGradient(string name, Color startColor, Color endColor, int width = 256, int height = 256)
        {
            Mat mat = new Mat(height, width, MatType.CV_8UC4);

            // Convert colors to BGRA (OpenCV format)
            Vec4b bgraStart = new Vec4b(startColor.B, startColor.G, startColor.R, startColor.A);
            Vec4b bgraEnd = new Vec4b(endColor.B, endColor.G, endColor.R, endColor.A);

            // Create vertical gradient
            for (int y = 0; y < height; y++)
            {
                float t = y / (float)(height - 1);
                Vec4b color = new Vec4b(
                    (byte)(bgraStart[0] + (bgraEnd[0] - bgraStart[0]) * t),
                    (byte)(bgraStart[1] + (bgraEnd[1] - bgraStart[1]) * t),
                    (byte)(bgraStart[2] + (bgraEnd[2] - bgraStart[2]) * t),
                    (byte)(bgraStart[3] + (bgraEnd[3] - bgraStart[3]) * t)
                );

                mat.Row(y).SetTo(color);
            }

            return CreateTextureFromMat(name, mat);
        }

        /// <summary>
        /// Generates a checkerboard texture.
        /// </summary>
        public static Texture GenerateCheckerboard(string name, Color color1, Color color2, int tileSize = 32, int width = 256, int height = 256)
        {
            Mat mat = new Mat(height, width, MatType.CV_8UC4);

            // Convert colors to BGRA (OpenCV format)
            Vec4b bgraColor1 = new Vec4b(color1.B, color1.G, color1.R, color1.A);
            Vec4b bgraColor2 = new Vec4b(color2.B, color2.G, color2.R, color2.A);

            // Draw checkerboard pattern
            for (int y = 0; y < height; y += tileSize)
            {
                for (int x = 0; x < width; x += tileSize)
                {
                    bool isColor1 = ((x / tileSize) + (y / tileSize)) % 2 == 0;
                    var color = isColor1 ? bgraColor1 : bgraColor2;

                    Rect roi = new Rect(x, y, Math.Min(tileSize, width - x), Math.Min(tileSize, height - y));
                    mat[roi].SetTo(color);
                }
            }

            return CreateTextureFromMat(name, mat);
        }

        /// <summary>
        /// Adds a default texture for a mesh type to the TextureManager.
        /// </summary>
        public static void AddDefaultTextureForMesh(string meshName)
        {
            Texture texture;
            switch (meshName)
            {
                case "cube":
                    texture = GenerateCheckerboard($"{meshName}_texture", Color.Black, Color.White);
                    break;
                case "plane":
                    texture = GenerateGradient($"{meshName}_texture", Color.Blue, Color.White);
                    break;
                case "sphere":
                    texture = GenerateSolidColor($"{meshName}_texture", Color.Green);
                    break;
                default:
                    throw new Exception($"No default texture available for mesh '{meshName}'.");
            }

            TextureManager.AddTexture(texture);
        }

        /// <summary>
        /// Utility to add a generated texture to the TextureManager.
        /// </summary>
        public static void AddTextureToManager(string name, Texture texture)
        {
            TextureManager.AddTexture(texture);
        }

        /// <summary>
        /// Creates an OpenGL texture from an OpenCV Mat.
        /// </summary>
        private static Texture CreateTextureFromMat(string name, Mat mat)
        {
            // Convert to RGBA format expected by OpenGL
            Cv2.CvtColor(mat, mat, ColorConversionCodes.BGRA2RGBA);

            // Flip vertically to match OpenGL's texture coordinate system
            Cv2.Flip(mat, mat, FlipMode.Y);

            int textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId);

            // Upload texture data
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                mat.Width,
                mat.Height,
                0,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
                PixelType.UnsignedByte,
                mat.Data
            );

            // Set default parameters
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            return new Texture(name, textureId);
        }
    }
}
