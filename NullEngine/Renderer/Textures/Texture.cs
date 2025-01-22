using System;
using OpenCvSharp;
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

            // Load the image using OpenCV
            using (Mat mat = new Mat(filePath, ImreadModes.Unchanged))
            {
                if (mat.Empty())
                    throw new ArgumentException($"Could not load texture file: {filePath}");

                int channels = mat.Channels();
                if (channels == 1)
                {
                    Cv2.CvtColor(mat, mat, ColorConversionCodes.GRAY2RGBA);
                }
                else if (channels == 3)
                {
                    Cv2.CvtColor(mat, mat, ColorConversionCodes.BGR2RGBA);
                }
                else if (channels == 4)
                {
                    Cv2.CvtColor(mat, mat, ColorConversionCodes.BGRA2RGBA);
                }
                else
                {
                    throw new NotSupportedException($"Unsupported number of channels: {channels}");
                }


                GL.TexImage2D(
                    TextureTarget.Texture2D,
                    0,
                    PixelInternalFormat.Rgba,
                    mat.Width,
                    mat.Height,
                    0,
                    OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    mat.Data);
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
