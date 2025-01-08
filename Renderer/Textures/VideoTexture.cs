using System;
using NullEngine.Utils;
using OpenTK.Graphics.OpenGL;

namespace NullEngine.Renderer.Textures
{
    public class VideoTexture : Texture
    {
        private VideoReader videoReader;
        private double timeSinceLastFrame;
        private double frameInterval;

        public VideoTexture(string name, string videoFilePath, bool generateMipmaps = true)
            : base(name, GL.GenTexture())
        {
            videoReader = new VideoReader(videoFilePath);
            frameInterval = 1.0 / videoReader.Fps;

            // Initialize the texture with the first frame
            videoReader.ReadFrame();
            UpdateTextureFromVideoFrame();

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

            // If enough time has passed, update the texture with the next frame
            if (timeSinceLastFrame >= frameInterval)
            {
                int framesToSkip = (int)(timeSinceLastFrame / frameInterval);
                for (int i = 0; i < framesToSkip; i++)
                {
                    if (!videoReader.ReadFrame())
                    {
                        // If we reach the end of the video, loop back to the start
                        videoReader.Dispose();
                        videoReader = new VideoReader(videoReader.videoFile);
                        videoReader.ReadFrame();
                    }
                }

                UpdateTextureFromVideoFrame();
                timeSinceLastFrame -= framesToSkip * frameInterval;
            }
        }

        private void UpdateTextureFromVideoFrame()
        {
            GL.BindTexture(TextureTarget.Texture2D, textureId);
            GL.TexImage2D(
                TextureTarget.Texture2D,
                0,
                PixelInternalFormat.Rgba,
                videoReader.Width,
                videoReader.Height,
                0,
                OpenTK.Graphics.OpenGL.PixelFormat.Rgba,
                PixelType.UnsignedByte,
                videoReader.pinnedPtr
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
