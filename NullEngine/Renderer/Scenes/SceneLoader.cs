using System;
using System.IO;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTK.Mathematics;
using NullEngine.Renderer.Components;
using System.Linq;
using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Textures;
using BridgeSDK;

namespace NullEngine.Renderer.Scenes
{
    public class SceneData
    {
        // New properties
        public float CameraSize { get; set; }
        public float Focus { get; set; }
        public float Offset { get; set; }

        public TransformData Transform { get; set; }
        public List<MeshData> Meshes { get; set; }
    }

    public static class SceneLoader
    {
        /// <summary>
        /// Validates that all mesh names across all scenes are unique.
        /// </summary>
        /// <param name="scenesData">The dictionary of all scenes and their data.</param>
        private static void ValidateUniqueMeshNames(Dictionary<string, SceneData> scenesData)
        {
            var meshNames = new HashSet<string>();

            foreach (var kv in scenesData)
            {
                string sceneName = kv.Key;
                SceneData sceneData = kv.Value;

                if (sceneData.Meshes == null)
                    continue;

                foreach (var mesh in sceneData.Meshes)
                {
                    if (!meshNames.Add(mesh.MeshName))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate MeshName '{mesh.MeshName}' found in scene '{sceneName}'. Mesh names must be globally unique."
                        );
                    }
                }
            }

            Log.Debug("All mesh names are unique across all scenes.");
        }

        /// <summary>
        /// Loads scenes from a JSON file and validates their mesh names.
        /// </summary>
        public static void LoadScenesFromJson(string filePath, BridgeWindowData bridgeData)
        {
            Log.Info($"Loading scenes from '{filePath}'.");

            if (!File.Exists(filePath))
            {
                Log.Error($"Scene file not found: {filePath}");
                throw new FileNotFoundException($"Scene file not found: {filePath}");
            }

            string json;
            try
            {
                json = File.ReadAllText(filePath);
                Log.Debug($"Successfully read file '{filePath}'.");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to read file '{filePath}': {ex.Message}");
                throw;
            }

            Dictionary<string, SceneData> scenesData;
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                scenesData = JsonSerializer.Deserialize<Dictionary<string, SceneData>>(json, options);
                Log.Debug($"Successfully deserialized JSON into {scenesData.Count} scenes.");
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to deserialize JSON: {ex.Message}");
                throw;
            }

            // Validate unique mesh names globally
            ValidateUniqueMeshNames(scenesData);

            foreach (var kv in scenesData)
            {
                string sceneName = kv.Key;
                SceneData sceneData = kv.Value;

                Log.Info($"Processing scene '{sceneName}'.");

                // Create the top-level scene transform
                Transform sceneTransform = MeshFactory.CreateTransform(sceneData.Transform, sceneName, "Scene");

                Scene scene = new Scene(
                    bridgeData,
                    sceneTransform,
                    sceneData.CameraSize,
                    sceneData.Focus,
                    sceneData.Offset
                );

                // Create all meshes via the new MeshFactory
                List<BaseMesh> meshes = MeshFactory.CreateMeshes(sceneData.Meshes, sceneName);

                // Add the meshes to the scene
                foreach (var mesh in meshes)
                {
                    scene.AddMesh(mesh);
                }

                // Finally, add scene to SceneManager
                SceneManager.AddScene(sceneName, scene);
                Log.Info($"Scene '{sceneName}' added to SceneManager.");
            }

            Log.Info("Scene loading complete.");
        }
    }
}