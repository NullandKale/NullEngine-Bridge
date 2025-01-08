using System;
using System.Drawing;
using System.Drawing.Imaging;
using OpenTK.Graphics.OpenGL;

namespace NullEngine.Renderer.Textures
{
    public class Texture : IDisposable
    {
        public string Name;
        protected int textureId;

        public Texture(string name, string filePath, bool generateMipmaps = true)
        {
            Name = name;
            textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId);

            // Load the image
            using (Bitmap bitmap = new Bitmap(filePath))
            {
                BitmapData data = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly,
                    System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba,
                    data.Width,
                    data.Height,
                    0,
                    OpenTK.Graphics.OpenGL.PixelFormat.Bgra,
                    PixelType.UnsignedByte,
                    data.Scan0);

                bitmap.UnlockBits(data);
            }

            // Set default texture parameters
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            if (generateMipmaps)
            {
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }


        public Texture(string name, int textureId)
        {
            Name = name;
            this.textureId = textureId;
        }


        public void Bind(TextureUnit unit = TextureUnit.Texture0)
        {
            GL.ActiveTexture(unit);
            GL.BindTexture(TextureTarget.Texture2D, textureId);
        }

        public void SetParameter(TextureParameterName parameter, int value)
        {
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.TexParameter(TextureTarget.Texture2D, parameter, value);
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public virtual void Dispose()
        {
            if (textureId != 0)
            {
                GL.DeleteTexture(textureId);
                textureId = 0;
            }
        }
    }
}
