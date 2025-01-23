using System;
using NullEngine.Utils;
using OpenTK.Graphics.OpenGL;

namespace NullEngine.Renderer.Textures
{
    public class VideoTexture : Texture
    {
        private AsyncVideoReader videoReader;
        private double timeSinceLastFrame;
        private double frameInterval;

        public VideoTexture(string name, string videoFilePath, bool generateMipmaps = true)
            : base(name, GL.GenTexture())
        {
            videoReader = new AsyncVideoReader(videoFilePath);
            frameInterval = 1.0 / videoReader.Fps;

            // Set default texture parameters
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Nearest);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapS, (int)TextureWrapMode.Repeat);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureWrapT, (int)TextureWrapMode.Repeat);

            if (generateMipmaps)
            {
                GL.GenerateMipmap(GenerateMipmapTarget.Texture2D);
            }

            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public void Update(double deltaTime)
        {
            timeSinceLastFrame += deltaTime;

            if (timeSinceLastFrame >= frameInterval)
            {
                UpdateTextureFromVideoFrame();
                timeSinceLastFrame -= frameInterval;
            }
        }

        private void UpdateTextureFromVideoFrame()
        {
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgb,       // Use RGB internal format
                videoReader.Width,
                videoReader.Height,
                0,
                PixelFormat.Bgr, // Use BGR pixel format
                PixelType.UnsignedByte,
                videoReader.GetCurrentFramePtr()      // Use the raw BGR data from the Mat
            );
            GL.BindTexture(TextureTarget.Texture2D, 0);
        }

        public override void Dispose()
        {
            base.Dispose();
            videoReader?.Dispose();
        }
    }
}
