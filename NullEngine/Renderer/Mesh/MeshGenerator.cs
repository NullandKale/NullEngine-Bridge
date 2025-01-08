using System;
using System.Collections.Generic;
using OpenTK.Mathematics;

namespace NullEngine.Renderer.Mesh
{
    public static class MeshGenerator
    {
        /// <summary>
        /// Generates a cube mesh.
        /// </summary>
        public static (float[] vertices, uint[] indices) GenerateCube(float size = 1.0f)
        {
            float halfSize = size / 2.0f;

            // 8 floats per vertex:
            //   position.x, position.y, position.z,
            //   normal.x,   normal.y,   normal.z,
            //   uv.x,       uv.y

            float[] vertices = new float[]
            {
        // Front face
        // position            normal       uv
        -halfSize, -halfSize,  halfSize,   0.0f, 0.0f, 1.0f,   0.0f, 0.0f,  // bottom-left
         halfSize, -halfSize,  halfSize,   0.0f, 0.0f, 1.0f,   1.0f, 0.0f,  // bottom-right
         halfSize,  halfSize,  halfSize,   0.0f, 0.0f, 1.0f,   1.0f, 1.0f,  // top-right
        -halfSize,  halfSize,  halfSize,   0.0f, 0.0f, 1.0f,   0.0f, 1.0f,  // top-left

        // Back face
        // position            normal       uv
        -halfSize, -halfSize, -halfSize,   0.0f, 0.0f, -1.0f,  0.0f, 0.0f,
         halfSize, -halfSize, -halfSize,   0.0f, 0.0f, -1.0f,  1.0f, 0.0f,
         halfSize,  halfSize, -halfSize,   0.0f, 0.0f, -1.0f,  1.0f, 1.0f,
        -halfSize,  halfSize, -halfSize,   0.0f, 0.0f, -1.0f,  0.0f, 1.0f,

        // Left face
        -halfSize, -halfSize, -halfSize,  -1.0f, 0.0f, 0.0f,   0.0f, 0.0f,
        -halfSize, -halfSize,  halfSize,  -1.0f, 0.0f, 0.0f,   1.0f, 0.0f,
        -halfSize,  halfSize,  halfSize,  -1.0f, 0.0f, 0.0f,   1.0f, 1.0f,
        -halfSize,  halfSize, -halfSize,  -1.0f, 0.0f, 0.0f,   0.0f, 1.0f,

        // Right face
         halfSize, -halfSize, -halfSize,   1.0f, 0.0f, 0.0f,   0.0f, 0.0f,
         halfSize, -halfSize,  halfSize,   1.0f, 0.0f, 0.0f,   1.0f, 0.0f,
         halfSize,  halfSize,  halfSize,   1.0f, 0.0f, 0.0f,   1.0f, 1.0f,
         halfSize,  halfSize, -halfSize,   1.0f, 0.0f, 0.0f,   0.0f, 1.0f,

        // Top face
        -halfSize,  halfSize,  halfSize,   0.0f, 1.0f, 0.0f,   0.0f, 0.0f,
         halfSize,  halfSize,  halfSize,   0.0f, 1.0f, 0.0f,   1.0f, 0.0f,
         halfSize,  halfSize, -halfSize,   0.0f, 1.0f, 0.0f,   1.0f, 1.0f,
        -halfSize,  halfSize, -halfSize,   0.0f, 1.0f, 0.0f,   0.0f, 1.0f,

        // Bottom face
        -halfSize, -halfSize, -halfSize,   0.0f, -1.0f, 0.0f,  0.0f, 0.0f,
         halfSize, -halfSize, -halfSize,   0.0f, -1.0f, 0.0f,  1.0f, 0.0f,
         halfSize, -halfSize,  halfSize,   0.0f, -1.0f, 0.0f,  1.0f, 1.0f,
        -halfSize, -halfSize,  halfSize,   0.0f, -1.0f, 0.0f,  0.0f, 1.0f
            };

            uint[] indices = new uint[]
            {
        // Front face
        0, 1, 2, 2, 3, 0,
        // Back face
        4, 5, 6, 6, 7, 4,
        // Left face
        8, 9, 10, 10, 11, 8,
        // Right face
        12, 13, 14, 14, 15, 12,
        // Top face
        16, 17, 18, 18, 19, 16,
        // Bottom face
        20, 21, 22, 22, 23, 20
            };

            return (vertices, indices);
        }


        /// <summary>
        /// Generates a plane mesh with a specified grid size.
        /// </summary>
        public static (float[] vertices, uint[] indices) GeneratePlane(float width = 1.0f, float depth = 1.0f, int gridX = 1, int gridZ = 1)
        {
            float stepX = width / gridX;
            float stepZ = depth / gridZ;
            float halfWidth = width / 2.0f;
            float halfDepth = depth / 2.0f;

            List<float> vertices = new List<float>();
            List<uint> indices = new List<uint>();

            for (int z = 0; z <= gridZ; z++)
            {
                for (int x = 0; x <= gridX; x++)
                {
                    float posX = -halfWidth + x * stepX;
                    float posZ = -halfDepth + z * stepZ;

                    // Calculate UV coordinates based on grid position
                    float u = (float)x / gridX;
                    float v = (float)z / gridZ;

                    vertices.AddRange(new float[]
                    {
                        posX, 0.0f, posZ,  // Position
                        0.0f, 1.0f, 0.0f,  // Normal (upwards)
                        u, v               // UV coordinates
                    });
                }
            }

            for (int z = 0; z < gridZ; z++)
            {
                for (int x = 0; x < gridX; x++)
                {
                    uint topLeft = (uint)(z * (gridX + 1) + x);
                    uint topRight = topLeft + 1;
                    uint bottomLeft = topLeft + (uint)(gridX + 1);
                    uint bottomRight = bottomLeft + 1;

                    indices.Add(topLeft);
                    indices.Add(bottomLeft);
                    indices.Add(topRight);

                    indices.Add(topRight);
                    indices.Add(bottomLeft);
                    indices.Add(bottomRight);
                }
            }

            return (vertices.ToArray(), indices.ToArray());
        }

        /// <summary>
        /// Generates a sphere mesh.
        /// </summary>
        public static (float[] vertices, uint[] indices) GenerateSphere(float radius = 1.0f, int stacks = 16, int slices = 16)
        {
            List<float> vertices = new List<float>();
            List<uint> indices = new List<uint>();

            for (int stack = 0; stack <= stacks; ++stack)
            {
                float phi = MathF.PI / 2 - stack * MathF.PI / stacks;
                float y = radius * MathF.Sin(phi);
                float xz = radius * MathF.Cos(phi);

                for (int slice = 0; slice <= slices; ++slice)
                {
                    float theta = slice * 2 * MathF.PI / slices;
                    float x = xz * MathF.Cos(theta);
                    float z = xz * MathF.Sin(theta);

                    vertices.AddRange(new float[]
                    {
                        x, y, z,  // Position
                        x / radius, y / radius, z / radius,  // Normal
                        (float)slice / slices, (float)stack / stacks // UV coordinates
                    });
                }
            }

            for (int stack = 0; stack < stacks; ++stack)
            {
                for (int slice = 0; slice < slices; ++slice)
                {
                    uint first = (uint)(stack * (slices + 1) + slice);
                    uint second = first + (uint)(slices + 1);

                    indices.Add(first);
                    indices.Add(second);
                    indices.Add(first + 1);

                    indices.Add(second);
                    indices.Add(second + 1);
                    indices.Add(first + 1);
                }
            }

            return (vertices.ToArray(), indices.ToArray());
        }
    }
}
