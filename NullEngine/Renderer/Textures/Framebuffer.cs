using OpenTK.Graphics.OpenGL;
using OpenCvSharp;
using System;
using System.Runtime.InteropServices;

namespace NullEngine.Renderer.Textures
{
    public class Framebuffer
    {
        public int FramebufferId;
        public int TextureId;
        public int DepthBufferId;
        private int Width;
        private int Height;

        public Framebuffer(int width, int height)
        {
            Width = width;
            Height = height;

            // Generate framebuffer
            GL.GenFramebuffers(1, out int framebuffer);
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, framebuffer);
            FramebufferId = framebuffer;

            // Generate texture
            GL.GenTextures(1, out int texture);
            GL.BindTexture(TextureTarget.Texture2D, texture);
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba,
                        width, height, 0, PixelFormat.Rgba, PixelType.UnsignedByte, IntPtr.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter,
                        (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter,
                        (int)TextureMagFilter.Linear);
            TextureId = texture;

            // Attach texture to framebuffer
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer,
                        FramebufferAttachment.ColorAttachment0,
                        TextureTarget.Texture2D, texture, 0);

            // Generate depth buffer
            GL.GenRenderbuffers(1, out int depthBuffer);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthBuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer,
                        RenderbufferStorage.DepthComponent32, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer,
                        FramebufferAttachment.DepthAttachment,
                        RenderbufferTarget.Renderbuffer, depthBuffer);
            DepthBufferId = depthBuffer;

            // Check completeness
            var status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                throw new Exception($"Framebuffer incomplete: {status}");
            }

            // Unbind resources
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
            GL.BindTexture(TextureTarget.Texture2D, 0);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, 0);
        }

        public void Bind()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, FramebufferId);
        }

        public void Unbind()
        {
            GL.BindFramebuffer(FramebufferTarget.Framebuffer, 0);
        }

        public void Cleanup()
        {
            GL.DeleteFramebuffers(1, ref FramebufferId);
            GL.DeleteTextures(1, ref TextureId);
            GL.DeleteRenderbuffers(1, ref DepthBufferId);
        }

        public void Capture(string fileName)
        {
            Bind();

            // Allocate unmanaged memory for pixel data
            int bufferSize = Width * Height * 4; // 4 bytes per pixel (RGBA)
            IntPtr pixels = Marshal.AllocHGlobal(bufferSize);

            try
            {
                // Read pixels from OpenGL (BGRA format)
                GL.ReadPixels(0, 0, Width, Height, PixelFormat.Bgra,
                            PixelType.UnsignedByte, pixels);

                // Create OpenCV Mat with the pixel data
                using (Mat mat = Mat.FromPixelData(Height, Width, MatType.CV_8UC4, pixels))
                {
                    // Flip vertically (OpenGL reads from bottom-left origin)
                    Cv2.Flip(mat, mat, FlipMode.Y);

                    // Convert from BGRA to BGR if needed (remove alpha channel)
                    // Cv2.CvtColor(mat, mat, ColorConversionCodes.BGRA2BGR);

                    // Save the image
                    Cv2.ImWrite(fileName, mat);
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pixels);
            }

            Unbind();
        }
    }
}
