using System;
using System.Collections.Generic;

namespace NullEngine.Renderer.Textures
{
    public static class TextureManager
    {
        private static Dictionary<string, Texture> textures = new Dictionary<string, Texture>();

        /// <summary>
        /// Loads a texture from a file and adds it to the manager.
        /// </summary>
        public static void LoadTexture(string name, string filePath, bool generateMipmaps = true)
        {
            if (textures.ContainsKey(name))
            {
                textures[name].Dispose();
            }

            try
            {
                textures[name] = new Texture(name, filePath, generateMipmaps);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to load texture '{name}' from '{filePath}': {ex.Message}");
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
    }
}
