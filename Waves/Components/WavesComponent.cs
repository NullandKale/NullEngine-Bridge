using NullEngine.Renderer.Components;
using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Shaders;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;

namespace Waves.Components
{
    public class WavesComponent : IComponent
    {
        public Shader WavesShader;
        public float timer = 0;

        // Reduce amplitudes to make waves less tall
        float[] amplitude = new float[] { 0.02f, 0.01f, 0.008f, 0.01f };

        // Keep roughly the same wavelengths, or tweak if you prefer
        float[] wavelength = new float[] { 1.1f, 1.5f, 2.0f, 0.2f };

        // Reduce speed to slow down wave motion
        float[] speed = new float[] { 0.3f, 0.25f, 0.2f, 0.15f };

        // Directions in XZ plane (normalized so they don't produce huge crosswise waves)
        Vector2[] waveDirs = new Vector2[]
        {
            new Vector2(1.0f, 0.0f).Normalized(),
            new Vector2(-0.5f, 0.5f).Normalized(),
            new Vector2(0.3f, 0.7f).Normalized(),
            new Vector2(-1.0f, -0.2f).Normalized()
        };

        public WavesComponent()
        {
            WavesShader = new Shader(
            """
            #version 330 core

            layout (location = 0) in vec3 inPosition;
            layout (location = 1) in vec3 inNormal;
            layout (location = 2) in vec2 inTexCoords;

            out vec3 fragNormal;        // Wave normal in world space (after we transform it)
            out vec3 fragWorldPos;      // Final vertex position in world space
            out vec2 fragTexCoords;
            out float waveHeight;       // Based on local Y displacement, ignoring world transform

            uniform mat4 model;
            uniform mat4 view;
            uniform mat4 projection;
            uniform float timer;

            const int MAX_WAVES = 4;
            uniform float amplitude[MAX_WAVES];
            uniform float wavelength[MAX_WAVES];
            uniform float speed[MAX_WAVES];
            uniform vec2 direction[MAX_WAVES];

            void main()
            {
                //--------------------------------------------------------
                // (1) Compute wave displacement entirely in LOCAL space.
                //--------------------------------------------------------
                vec3 localPos = inPosition;
                vec3 totalLocalDisp = vec3(0.0);

                for (int i = 0; i < MAX_WAVES; i++)
                {
                    float k         = 2.0 * 3.14159 / wavelength[i];
                    float w         = sqrt(9.8 * k) * speed[i];
                    float wavePhase = dot(direction[i], localPos.xz) * k - w * (timer * 0.5);

                    float waveFactor    = amplitude[i] * cos(wavePhase);
                    float displacementY = amplitude[i] * sin(wavePhase);

                    // Assume "up" is the local Y axis:
                    totalLocalDisp.x += direction[i].x * waveFactor;
                    totalLocalDisp.y += displacementY;
                    totalLocalDisp.z += direction[i].y * waveFactor;
                }

                // Final displaced position in local space
                vec3 finalLocalPos = localPos + totalLocalDisp;

                //--------------------------------------------------------
                // (2) Compute partial derivatives in LOCAL space for the normal
                //--------------------------------------------------------
                // If there were no waves, dPos/dx = (1, 0, 0) and dPos/dz = (0, 0, 1).
                vec3 dPosdx = vec3(1.0, 0.0, 0.0);
                vec3 dPosdz = vec3(0.0, 0.0, 1.0);

                for (int i = 0; i < MAX_WAVES; i++)
                {
                    float k         = 2.0 * 3.14159 / wavelength[i];
                    float w         = sqrt(9.8 * k) * speed[i];
                    float wavePhase = dot(direction[i], localPos.xz) * k - w * (timer * 0.5);

                    float sinP = sin(wavePhase);
                    float cosP = cos(wavePhase);

                    float dWavePhase_dx = k * direction[i].x;  
                    float dWavePhase_dz = k * direction[i].y;

                    // Y displacement partial derivatives
                    float dDispY_dx = amplitude[i] * cosP * dWavePhase_dx;
                    float dDispY_dz = amplitude[i] * cosP * dWavePhase_dz;

                    // X displacement partials
                    float dDispX_dx = direction[i].x * (amplitude[i] * -sinP * dWavePhase_dx);
                    float dDispX_dz = direction[i].x * (amplitude[i] * -sinP * dWavePhase_dz);

                    // Z displacement partials
                    float dDispZ_dx = direction[i].y * (amplitude[i] * -sinP * dWavePhase_dx);
                    float dDispZ_dz = direction[i].y * (amplitude[i] * -sinP * dWavePhase_dz);

                    // Update partial derivatives
                    dPosdx.x += dDispX_dx;  
                    dPosdx.y += dDispY_dx;
                    dPosdx.z += dDispZ_dx;

                    dPosdz.x += dDispX_dz;
                    dPosdz.y += dDispY_dz;
                    dPosdz.z += dDispZ_dz;
                }

                // Local‐space normal
                vec3 localNormal = normalize(cross(dPosdx, dPosdz));

                //--------------------------------------------------------
                // (3) Map local Y displacement to [0..1] for color
                //--------------------------------------------------------
                float sumAmp = 0.0;
                for (int i = 0; i < MAX_WAVES; i++)
                {
                    sumAmp += amplitude[i];
                }
                // totalLocalDisp.y is how much we moved in local Y
                // We'll just do the "peak to trough" mapping from [-sumAmp..sumAmp] to [0..1].
                float localHeightNorm = (totalLocalDisp.y + sumAmp) / (2.0 * sumAmp);
                localHeightNorm = clamp(localHeightNorm, 0.0, 1.0);

                //--------------------------------------------------------
                // (4) Transform finalLocalPos & localNormal into world space
                //--------------------------------------------------------
                // Final position in world space:
                vec4 worldPos4 = model * vec4(finalLocalPos, 1.0);
                vec3 worldPos  = worldPos4.xyz;

                // Transform the local normal by the model's normal matrix
                // mat3(model) might suffice if there's no non-uniform scale.
                // For correctness with non-uniform scale, you'd typically use:
                // mat3 normalMatrix = transpose(inverse(mat3(model)));
                vec3 worldNormal = normalize(mat3(model) * localNormal);

                //--------------------------------------------------------
                // (5) Write out to the pipeline
                //--------------------------------------------------------
                gl_Position   = projection * view * worldPos4;

                fragWorldPos  = worldPos;
                fragNormal    = worldNormal;
                fragTexCoords = inTexCoords;
                waveHeight    = localHeightNorm;  // purely local-based
            }
            
            """,
            """
            #version 330 core

            in vec3 fragNormal;
            in vec3 fragWorldPos;
            in float waveHeight; // purely from local displacement
            out vec4 FragColor;

            const int colorMode = 0;

            // Directional light
            const vec3 LIGHT_DIR       = normalize(vec3(0.8, 1.0, 0.3));

            // Colors for wave gradient
            const vec3 WAVE_LOW_COLOR  = vec3(0.0, 0.2, 0.6);
            const vec3 WAVE_HIGH_COLOR = vec3(0.2, 0.8, 1.0);

            void main()
            {
                if (colorMode == 0)
                {
                    // Blend wave colors based on local waveHeight
                    vec3 waveColor = mix(WAVE_LOW_COLOR, WAVE_HIGH_COLOR, waveHeight);

                    // Simple Lambert shading
                    vec3 N = normalize(fragNormal);
                    float lambert = max(dot(N, LIGHT_DIR), 0.0);

                    float ambient = 0.7;
                    float diffuse = 0.3 * lambert;
                    float lighting = ambient + diffuse;

                    vec3 finalColor = waveColor * lighting;
                    FragColor = vec4(finalColor, 1.0);
                }
                else if (colorMode == 1)
                {
                    // Debug: waveHeight in red
                    // waveHeight is local-based, so rotating the mesh won't change the distribution
                    FragColor = vec4(waveHeight, 0.0, 0.0, 1.0);
                }
                else if (colorMode == 2)
                {
                    // Debug: visualize normal
                    vec3 normalColor = normalize(fragNormal) * 0.5 + 0.5;
                    FragColor = vec4(normalColor, 1.0);
                }
                else if (colorMode == 3)
                {
                    // Debug: grayscale based on worldPos.y
                    // Rotating the mesh might shift this because it's truly in world space,
                    // but waveHeight remains unaffected
                    float gradient = clamp((fragWorldPos.y + 10.0) / 20.0, 0.0, 1.0);
                    FragColor = vec4(vec3(gradient), 1.0);
                }
                else
                {
                    // Fallback
                    FragColor = vec4(1.0, 0.0, 1.0, 1.0);
                }
            }
            
            """);
        }

        public object Clone()
        {
            return new WavesComponent();
        }

        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {
            // Not used in this example
        }

        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {
            // Not used in this example
        }

        public void Update(BaseMesh mesh, float deltaTime)
        {
            timer += deltaTime;

            // Use our waves shader for the mesh
            mesh.shader = WavesShader;

            // Send basic uniform data
            mesh.shader.SetUniform("timer", timer);
            mesh.shader.SetUniform("deltaTime", deltaTime);

            // Send array parameters
            mesh.shader.SetUniformArray("amplitude", amplitude);
            mesh.shader.SetUniformArray("wavelength", wavelength);
            mesh.shader.SetUniformArray("speed", speed);

            // Send the wave directions as vec2
            for (int i = 0; i < waveDirs.Length; i++)
            {
                mesh.shader.SetUniform($"direction[{i}]", waveDirs[i]);
            }
        }
    }
}
