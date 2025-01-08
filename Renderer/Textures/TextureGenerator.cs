using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using OpenTK.Graphics.OpenGL;

namespace NullEngine.Renderer.Textures
{
    public static class TextureGenerator
    {
        /// <summary>
        /// Generates a solid color texture.
        /// </summary>
        public static Texture GenerateSolidColor(string name, Color color, int width = 256, int height = 256)
        {
            Bitmap bitmap = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.Clear(color);
            }

            return CreateTextureFromBitmap(name, bitmap);
        }

        /// <summary>
        /// Generates a gradient texture.
        /// </summary>
        public static Texture GenerateGradient(string name, Color startColor, Color endColor, int width = 256, int height = 256)
        {
            Bitmap bitmap = new Bitmap(width, height);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(0, 0, width, height), startColor, endColor, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, 0, 0, width, height);
                }
            }

            return CreateTextureFromBitmap(name, bitmap);
        }

        /// <summary>
        /// Generates a checkerboard texture.
        /// </summary>
        public static Texture GenerateCheckerboard(string name, Color color1, Color color2, int tileSize = 32, int width = 256, int height = 256)
        {
            Bitmap bitmap = new Bitmap(width, height);
            for (int y = 0; y < height; y += tileSize)
            {
                for (int x = 0; x < width; x += tileSize)
                {
                    bool isColor1 = (x / tileSize + y / tileSize) % 2 == 0;
                    using (Graphics g = Graphics.FromImage(bitmap))
                    {
                        g.FillRectangle(
                            new SolidBrush(isColor1 ? color1 : color2),
                            x, y, tileSize, tileSize);
                    }
                }
            }

            return CreateTextureFromBitmap(name, bitmap);
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
        /// Creates an OpenGL texture from a bitmap.
        /// </summary>
        private static Texture CreateTextureFromBitmap(string name, Bitmap bitmap)
        {
            int textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId);

            // Upload bitmap to GPU
            BitmapData data = bitmap.LockBits(
                new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                ImageLockMode.ReadOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);

            GL.TexImage2D(
                TextureTarget.Texture2D, 0,
                PixelInternalFormat.Rgba,
                data.Width, data.Height,
                0,
                OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
                PixelType.UnsignedByte, data.Scan0);

            bitmap.UnlockBits(data);

            // Set texture parameters
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);

            GL.BindTexture(TextureTarget.Texture2D, 0);

            // Create Texture instance
            return new Texture(name, textureId);
        }
    }
}
