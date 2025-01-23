using System;
using System.Collections.Generic;
using System.IO;

namespace NullEngine.Renderer.Textures
{
    public static class TextureManager
    {
        private static Dictionary<string, Texture> textures = new Dictionary<string, Texture>();

        /// <summary>
        /// Loads a texture from a file and adds it to the manager.
        /// If the file is a video, a VideoTexture is created.
        /// If the file is an image, a regular Texture is created.
        /// </summary>
        public static void LoadTexture(string name, string filePath, bool generateMipmaps = true)
        {
            if (textures.ContainsKey(name))
            {
                textures[name].Dispose();
            }

            try
            {
                string extension = Path.GetExtension(filePath).ToLower();
                if (IsVideoFile(extension))
                {
                    textures[name] = new VideoTexture(name, filePath, generateMipmaps);
                }
                else if (IsImageFile(extension))
                {
                    textures[name] = new Texture(name, filePath, generateMipmaps);
                }
                else
                {
                    throw new Exception($"Unsupported file type: {extension}");
                }
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load texture '{name}' from '{filePath}': {ex.Message}");
            }
        }

        public static void UpdateVideoTextures(float deltaTime)
        {
            foreach (var texture in textures.Values)
            {
                if (texture is VideoTexture videoTexture)
                {
                    videoTexture.Update(deltaTime);
                }
            }
        }

        /// <summary>
        /// Adds an existing texture to the manager.
        /// </summary>
        public static void AddTexture(Texture texture)
        {
            if (textures.ContainsKey(texture.Name))
            {
                throw new Exception($"Texture '{texture.Name}' already exists.");
            }

            textures[texture.Name] = texture;
        }

        /// <summary>
        /// Retrieves a texture by name.
        /// </summary>
        public static Texture GetTexture(string name)
        {
            if (textures.TryGetValue(name, out var texture))
            {
                return texture;
            }

            throw new Exception($"Texture '{name}' not found.");
        }

        /// <summary>
        /// Disposes all loaded textures and clears the manager.
        /// </summary>
        public static void Dispose()
        {
            foreach (var texture in textures.Values)
            {
                texture.Dispose();
            }
            textures.Clear();
        }

        public static bool HasTexture(string name)
        {
            return textures.ContainsKey(name);
        }

        private static bool IsVideoFile(string extension)
        {
            string[] videoExtensions = { ".mp4", ".avi", ".mov", ".mkv", ".wmv" };
            return Array.Exists(videoExtensions, ext => ext == extension);
        }

        private static bool IsImageFile(string extension)
        {
            string[] imageExtensions = { ".png", ".jpg", ".jpeg", ".bmp", ".tga", ".gif" };
            return Array.Exists(imageExtensions, ext => ext == extension);
        }
    }
}
