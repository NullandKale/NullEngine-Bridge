using System;
using OpenCvSharp;
using OpenTK.Graphics.OpenGL;

namespace NullEngine.Renderer.Textures
{
    public class Texture : IDisposable
    {
        public string Name;
        public int textureId;
        public int width;
        public int height;

        public Texture(string name, string filePath, bool generateMipmaps = false)
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
                    PixelFormat.Rgba,
                    PixelType.UnsignedByte,
                    mat.Data);

                this.width = mat.Width;
                this.height = mat.Height;
            }

            // Set default texture parameters
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Clamp);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Clamp);

            if (generateMipmaps)
            {
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        // New constructor: creates a texture from a pointer to raw image data.
        // The data is assumed to be in BGRA format (8 bits per channel).
        // The pointer is copied into the texture, so it does not need to remain valid afterwards.
        public Texture(string name, IntPtr data, int width, int height, bool swapRB = false, bool generateMipmaps = false)
        {
            Name = name;
            textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId);

            // Upload the raw image data to OpenGL.
            // Here we assume the provided data is in BGRA format.
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                width,
                height,
                0,
                swapRB ? PixelFormat.Rgba : PixelFormat.Bgra,
                PixelType.UnsignedByte,
                data);

            this.width = width;
            this.height = height;

            // Set default texture parameters.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Clamp);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Clamp);

            if (generateMipmaps)
            {
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public Texture(string name, int[] data, int width, int height, bool swapRB = false, bool generateMipmaps = false)
        {
            Name = name;
            textureId = GL.GenTexture();
            GL.BindTexture(TextureTarget.Texture2D, textureId);

            // Upload the raw image data to OpenGL.
            // Here we assume the provided data is in BGRA format.
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                width,
                height,
                0,
                swapRB ? PixelFormat.Rgba : PixelFormat.Bgra,
                PixelType.UnsignedByte,
                data);

            this.width = width;
            this.height = height;

            // Set default texture parameters.
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Clamp);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Clamp);

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
