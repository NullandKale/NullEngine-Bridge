using System;
using System.Collections.Generic;
using System.Drawing;
using NullEngine.Renderer.Textures;

namespace NullEngine.Renderer.Mesh
{
    public static class MeshManager
    {
        private static Dictionary<string, BaseMesh> meshes = new Dictionary<string, BaseMesh>();

        /// <summary>
        /// Preloads predefined meshes into the manager.
        /// </summary>
        public static void LoadMeshes()
        {
            // Generate or retrieve textures for each mesh type
            Texture cubeTexture = TextureGenerator.GenerateCheckerboard("cube_texture", Color.Black, Color.White);
            Texture planeTexture = TextureGenerator.GenerateGradient("plane_texture", Color.Blue, Color.White);
            Texture sphereTexture = TextureGenerator.GenerateSolidColor("sphere_texture", Color.Green);

            // Add textures to the TextureManager for reuse
            TextureManager.AddTexture(cubeTexture);
            TextureManager.AddTexture(planeTexture);
            TextureManager.AddTexture(sphereTexture);

            // Generate cube mesh with texture
            (float[] cubeVertices, uint[] cubeIndices) = MeshGenerator.GenerateCube();
            meshes["cube"] = new BaseMesh("cube", cubeVertices, cubeIndices, cubeTexture);

            // Generate plane mesh with texture
            (float[] planeVertices, uint[] planeIndices) = MeshGenerator.GeneratePlane(10, 10, 100, 100); // 100x100 grid
            meshes["plane"] = new BaseMesh("plane", planeVertices, planeIndices, planeTexture);

            // Generate sphere mesh with texture
            (float[] sphereVertices, uint[] sphereIndices) = MeshGenerator.GenerateSphere(1.0f, 32, 32); // More detailed sphere
            meshes["sphere"] = new BaseMesh("sphere", sphereVertices, sphereIndices, sphereTexture);
        }


        /// <summary>
        /// Adds a custom mesh to the manager.
        /// </summary>
        public static void AddMesh(string name, BaseMesh mesh)
        {
            if (meshes.ContainsKey(name))
            {
                throw new Exception($"Mesh with name '{name}' already exists.");
            }

            meshes[name] = mesh;
        }

        /// <summary>
        /// Retrieves a mesh by name.
        /// </summary>
        public static BaseMesh GetMesh(string name)
        {
            if (meshes.TryGetValue(name, out BaseMesh mesh))
            {
                return mesh;
            }

            throw new Exception($"Mesh with name '{name}' not found.");
        }

        /// <summary>
        /// Disposes all meshes and clears the manager.
        /// </summary>
        public static void Dispose()
        {
            foreach (var mesh in meshes.Values)
            {
                mesh.Dispose();
            }
            meshes.Clear();
        }
    }
}
