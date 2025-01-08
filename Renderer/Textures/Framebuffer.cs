using OpenTK.Graphics.OpenGL;
using System;
using System.Drawing;
using System.Drawing.Imaging;
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
            GL.TexImage2D(TextureTarget.Texture2D, 0, PixelInternalFormat.Rgba, width, height, 0, OpenTK.Graphics.OpenGL.PixelFormat.Rgba, PixelType.UnsignedByte, nint.Zero);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMinFilter, (int)TextureMinFilter.Linear);
            GL.TexParameter(TextureTarget.Texture2D, TextureParameterName.TextureMagFilter, (int)TextureMagFilter.Linear);
            TextureId = texture;

            // Attach texture to framebuffer
            GL.FramebufferTexture2D(FramebufferTarget.Framebuffer, FramebufferAttachment.ColorAttachment0, TextureTarget.Texture2D, texture, 0);

            // Generate depth buffer
            GL.GenRenderbuffers(1, out int depthBuffer);
            GL.BindRenderbuffer(RenderbufferTarget.Renderbuffer, depthBuffer);
            GL.RenderbufferStorage(RenderbufferTarget.Renderbuffer, RenderbufferStorage.DepthComponent32, width, height);
            GL.FramebufferRenderbuffer(FramebufferTarget.Framebuffer, FramebufferAttachment.DepthAttachment, RenderbufferTarget.Renderbuffer, depthBuffer);
            DepthBufferId = depthBuffer;

            // Check framebuffer completeness
            FramebufferErrorCode status = GL.CheckFramebufferStatus(FramebufferTarget.Framebuffer);
            if (status != FramebufferErrorCode.FramebufferComplete)
            {
                throw new Exception($"Framebuffer is incomplete: {status}");
            }

            // Unbind framebuffer
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
            if (FramebufferId != 0)
            {
                GL.DeleteFramebuffers(1, ref FramebufferId);
            }
            if (TextureId != 0)
            {
                GL.DeleteTextures(1, ref TextureId);
            }
            if (DepthBufferId != 0)
            {
                GL.DeleteRenderbuffers(1, ref DepthBufferId);
            }
        }

        public void Capture(string name)
        {
            Bind();

            // Allocate unmanaged memory for pixel data
            int pixelSize = 4; // 4 bytes per pixel (BGRA)
            nint pixelsPtr = Marshal.AllocHGlobal(Width * Height * pixelSize);

            try
            {
                // Read pixels into unmanaged memory
                GL.ReadPixels(0, 0, Width, Height, OpenTK.Graphics.OpenGL.PixelFormat.Bgra, PixelType.UnsignedByte, pixelsPtr);

                // Create a bitmap to hold the image
                Bitmap bitmap = new Bitmap(Width, Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                BitmapData data = bitmap.LockBits(new Rectangle(0, 0, Width, Height), ImageLockMode.WriteOnly, bitmap.PixelFormat);

                int stride = data.Stride;
                nint scan0 = data.Scan0;

                unsafe
                {
                    byte* pPixels = (byte*)pixelsPtr.ToPointer();

                    for (int y = 0; y < Height; y++)
                    {
                        for (int x = 0; x < Width; x++)
                        {
                            // Source pixel position
                            int sourceIndex = (y * Width + x) * pixelSize;
                            byte* sourcePixel = pPixels + sourceIndex;

                            // Destination pixel position
                            int destinationIndex = y * stride + x * pixelSize;
                            byte* destinationPixel = (byte*)scan0 + destinationIndex;

                            // Copy BGRA components
                            destinationPixel[0] = sourcePixel[0]; // B
                            destinationPixel[1] = sourcePixel[1]; // G
                            destinationPixel[2] = sourcePixel[2]; // R
                            destinationPixel[3] = sourcePixel[3]; // A
                        }
                    }
                }

                bitmap.UnlockBits(data);

                // Save the bitmap as a PNG
                bitmap.Save(name, ImageFormat.Png);
                bitmap.Dispose();
            }
            finally
            {
                // Free unmanaged memory
                Marshal.FreeHGlobal(pixelsPtr);
            }

            Unbind();
        }



    }
}
