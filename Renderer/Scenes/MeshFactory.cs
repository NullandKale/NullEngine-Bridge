using System;
using System.Collections.Generic;
using System.Text.Json;
using NullEngine.Renderer.Components;
using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Textures;
using OpenTK.Mathematics;

namespace NullEngine.Renderer.Scenes
{
    public class MeshData
    {
        public string MeshName { get; set; } // For predefined meshes
        public Dictionary<string, object> MeshParameters { get; set; } // For procedural meshes
        public TransformData Transform { get; set; }
        public List<ComponentData> Components { get; set; }
    }

    public class TransformData
    {
        public float[] Position { get; set; }
        public float[] Rotation { get; set; }
        public float[] Scale { get; set; }
    }

    public static class MeshFactory
    {
        /// <summary>
        /// Creates a list of meshes from a list of MeshData entries.
        /// </summary>
        /// <param name="meshDataList">The mesh data entries from the JSON.</param>
        /// <param name="sceneName">Name of the scene for logging/context.</param>
        /// <returns>A list of BaseMesh objects.</returns>
        public static List<BaseMesh> CreateMeshes(List<MeshData> meshDataList, string sceneName)
        {
            List<BaseMesh> createdMeshes = new List<BaseMesh>();

            if (meshDataList == null || meshDataList.Count == 0)
            {
                Log.Warn($"No mesh data found for scene '{sceneName}'.");
                return createdMeshes;
            }

            // NEW: Log how many meshes we’re about to process
            Log.Debug($"Creating {meshDataList.Count} meshes for scene '{sceneName}'.");

            foreach (var meshData in meshDataList)
            {
                // NEW: Log the incoming parameters
                var typeStr = (meshData.MeshParameters != null && meshData.MeshParameters.ContainsKey("Type"))
                              ? meshData.MeshParameters["Type"].ToString() : "null";
                Log.Debug($"Processing MeshData '{meshData.MeshName}' (type={typeStr}) in scene '{sceneName}'.");

                BaseMesh mesh = null;
                try
                {
                    mesh = CreateBaseMesh(meshData, sceneName);
                }
                catch (Exception ex)
                {
                    // If something truly goes wrong, log it so you see it in the output
                    Log.Error($"Exception while creating mesh '{meshData.MeshName}' in scene '{sceneName}': {ex}");
                }

                if (mesh != null)
                {
                    // Apply transform
                    mesh.Transform = CreateTransform(meshData.Transform, sceneName,
                                   meshData.MeshName ?? "UntitledMesh");

                    // Attach components
                    if (meshData.Components != null && meshData.Components.Count > 0)
                    {
                        foreach (var compData in meshData.Components)
                        {
                            var component = ComponentFactory.CreateComponent(compData.Type, compData.Properties);
                            if (component != null)
                            {
                                mesh.AddComponent(component);
                                Log.Debug($"Added component '{compData.Type}' to mesh '{meshData.MeshName}' in scene '{sceneName}'.");
                            }
                        }
                    }

                    createdMeshes.Add(mesh);
                    Log.Info($"Mesh '{meshData.MeshName}' added to scene '{sceneName}'.");
                }
                else
                {
                    // NEW: Log explicitly if mesh was null
                    Log.Warn($"Mesh creation returned null for '{meshData.MeshName}' in scene '{sceneName}'.");
                }
            }

            return createdMeshes;
        }

        /// <summary>
        /// Helper that decides how to create a single mesh from MeshData.
        /// </summary>
        private static BaseMesh CreateBaseMesh(MeshData meshData, string sceneName)
        {
            bool hasType = (meshData.MeshParameters != null)
                           && meshData.MeshParameters.ContainsKey("Type");

            if (hasType)
            {
                // 1) If we have a “Type”, it’s a brand-new procedural mesh, so generate and store it.
                Log.Info($"Generating procedural mesh '{meshData.MeshName}' in scene '{sceneName}'.");
                BaseMesh newMesh = GenerateProceduralMesh(meshData.MeshParameters, sceneName, meshData.MeshName);

                if (newMesh != null)
                {
                    // Important: store it under its MeshName so it can be referenced!
                    MeshManager.AddMesh(meshData.MeshName, newMesh);
                }
                else
                {
                    Log.Warn($"Failed to create procedural mesh '{meshData.MeshName}' in scene '{sceneName}'.");
                }

                return newMesh;
            }
            else if (!string.IsNullOrEmpty(meshData.MeshName))
            {
                // 2) If no “Type” but we have a MeshName, treat it as “predefined” or pre-loaded in the manager.
                Log.Info($"Fetching predefined mesh '{meshData.MeshName}' in scene '{sceneName}'.");
                return MeshManager.GetMesh(meshData.MeshName);
            }
            else
            {
                // 3) No Type, no name => nothing to create or fetch
                Log.Warn($"No valid mesh name or mesh parameters provided in scene '{sceneName}'. Returning null.");
                return null;
            }
        }


        /// <summary>
        /// Generates a procedural mesh (Plane, Cube, Sphere, etc.) based on the parameters dictionary.
        /// </summary>
        private static BaseMesh GenerateProceduralMesh(Dictionary<string, object> parameters, string sceneName, string meshName)
        {
            try
            {
                string type = parameters.ContainsKey("Type") ? parameters["Type"].ToString() : null;
                if (type == null)
                {
                    Log.Warn($"No 'Type' specified for procedural mesh '{meshName}' in scene '{sceneName}'.");
                    return null;
                }

                switch (type)
                {
                    case "Plane":
                        return GeneratePlane(parameters, sceneName, meshName);
                    case "Cube":
                        return GenerateCube(parameters, sceneName, meshName);
                    case "Sphere":
                        return GenerateSphere(parameters, sceneName, meshName);
                    default:
                        Log.Warn($"Unsupported procedural mesh type '{type}' for mesh '{meshName}' in scene '{sceneName}'.");
                        return null;
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to generate procedural mesh '{meshName}' in scene '{sceneName}': {ex.Message}");
                return null;
            }
        }


        private static string MakeUniqueTextureName(string baseName)
        {
            string candidate = baseName;
            int counter = 1;
            while (TextureManager.HasTexture(candidate))
            {
                candidate = $"{baseName}_{counter}";
                counter++;
            }
            return candidate;
        }

        private static BaseMesh GeneratePlane(Dictionary<string, object> parameters, string sceneName, string meshName)
        {
            float width = GetValueFromParameters(parameters, "Width", 1.0f);
            float depth = GetValueFromParameters(parameters, "Depth", 1.0f);
            int gridX = GetValueFromParameters(parameters, "GridX", 1);
            int gridZ = GetValueFromParameters(parameters, "GridZ", 1);

            (float[] vertices, uint[] indices) = MeshGenerator.GeneratePlane(width, depth, gridX, gridZ);

            string textureName = $"{meshName}_Texture";
            textureName = MakeUniqueTextureName(textureName);

            Texture texture = TextureGenerator.GenerateCheckerboard(
                textureName,
                System.Drawing.Color.Black,
                System.Drawing.Color.White
            );

            TextureManager.AddTexture(texture);

            Log.Debug(
              $"Generated plane with {gridX}x{gridZ} grid and texture '{textureName}' for scene '{sceneName}'."
            );
            return new BaseMesh(meshName, vertices, indices, texture);
        }


        private static BaseMesh GenerateCube(Dictionary<string, object> parameters, string sceneName, string meshName)
        {
            float size = GetValueFromParameters(parameters, "Size", 1.0f);

            (float[] vertices, uint[] indices) = MeshGenerator.GenerateCube(size);

            string textureName = $"{meshName}_Texture";
            textureName = MakeUniqueTextureName(textureName);

            Texture texture = TextureGenerator.GenerateCheckerboard(textureName, System.Drawing.Color.Gray, System.Drawing.Color.DarkGray);
            TextureManager.AddTexture(texture);

            Log.Debug($"Generated cube with size {size} and texture '{textureName}' for scene '{sceneName}'.");
            return new BaseMesh(meshName, vertices, indices, texture);
        }

        private static BaseMesh GenerateSphere(Dictionary<string, object> parameters, string sceneName, string meshName)
        {
            float radius = GetValueFromParameters(parameters, "Radius", 1.0f);
            int stacks = GetValueFromParameters(parameters, "Stacks", 16);
            int slices = GetValueFromParameters(parameters, "Slices", 16);

            (float[] vertices, uint[] indices) = MeshGenerator.GenerateSphere(radius, stacks, slices);

            string textureName = $"{meshName}_Texture";
            textureName = MakeUniqueTextureName(textureName);

            Texture texture = TextureGenerator.GenerateCheckerboard(textureName, System.Drawing.Color.Blue, System.Drawing.Color.LightBlue);
            TextureManager.AddTexture(texture);
            
            Log.Debug($"Generated sphere with radius {radius}, {stacks} stacks, {slices} slices, and texture '{textureName}' for scene '{sceneName}'.");
            return new BaseMesh(meshName, vertices, indices, texture);
        }


        public static Transform CreateTransform(TransformData transformData, string sceneName, string ownerName)
        {
            if (transformData == null)
            {
                Log.Warn($"No transform data provided for '{ownerName}' in scene '{sceneName}'. Using default transform.");
                return new Transform(Vector3.Zero, Vector3.Zero, Vector3.One);
            }

            // Safely create independent Vector3s for Position, Rotation, Scale
            var pos = SafeArrayToVector3(transformData.Position, Vector3.Zero, sceneName, ownerName, "Position");
            var rot = SafeArrayToVector3(transformData.Rotation, Vector3.Zero, sceneName, ownerName, "Rotation");
            var scl = SafeArrayToVector3(transformData.Scale, Vector3.One, sceneName, ownerName, "Scale");

            Transform transform = new Transform(pos, rot, scl);
            Log.Debug($"'{ownerName}' in scene '{sceneName}' transform: Pos={transform.Position}, Rot={transform.Rotation}, Scale={transform.Scale}");
            return transform;
        }

        private static Vector3 SafeArrayToVector3(float[] arr, Vector3 defaultVal, string sceneName, string ownerName, string fieldName)
        {
            if (arr == null || arr.Length < 3)
            {
                Log.Warn($"Invalid '{fieldName}' transform data for '{ownerName}' in scene '{sceneName}'. Using default {defaultVal}.");
                return defaultVal;
            }

            return new Vector3(arr[0], arr[1], arr[2]);
        }

        private static T GetValueFromParameters<T>(Dictionary<string, object> parameters, string key, T defaultValue)
        {
            if (parameters.ContainsKey(key))
            {
                object value = parameters[key];
                try
                {
                    // Handle JsonElement case
                    if (value is JsonElement jsonElement)
                    {
                        if (typeof(T) == typeof(float) && jsonElement.TryGetSingle(out float floatValue))
                            return (T)(object)floatValue;

                        if (typeof(T) == typeof(int) && jsonElement.TryGetInt32(out int intValue))
                            return (T)(object)intValue;
                    }

                    // General conversion
                    return (T)Convert.ChangeType(value, typeof(T));
                }
                catch (Exception ex)
                {
                    Log.Warn($"Failed to convert parameter '{key}' to type '{typeof(T).Name}': {ex.Message}. Using default value '{defaultValue}'.");
                }
            }

            return defaultValue;
        }
    }
}
