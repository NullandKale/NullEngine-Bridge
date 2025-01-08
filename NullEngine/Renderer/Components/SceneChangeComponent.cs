using NullEngine.Renderer.Mesh;
using NullEngine.Renderer.Scenes;
using OpenTK.Mathematics;
using OpenTK.Windowing.GraphicsLibraryFramework;
using System;

namespace NullEngine.Renderer.Components
{
    public class SceneChangeComponent : IComponent
    {
        public string[] Scenes;

        public SceneChangeComponent()
        {
            Scenes = new string[10];
            for (int i = 0; i < Scenes.Length; i++)
            {
                Scenes[i] = ""; // Initialize with empty strings
            }
        }

        public SceneChangeComponent(string[] scenes)
        {
            if (scenes == null || scenes.Length != 10)
            {
                // If the scenes array is null or of incorrect length, create a default array
                Scenes = new string[10];
                for (int i = 0; i < Scenes.Length; i++)
                {
                    Scenes[i] = "";
                }
            }
            else
            {
                Scenes = (string[])scenes.Clone(); // Clone the provided array for safety
            }
        }

        public object Clone()
        {
            return new SceneChangeComponent(Scenes); // Create a new instance with cloned scene data
        }

        public void HandleKeyboardInput(BaseMesh mesh, KeyboardState keyboardState, float deltaTime)
        {
            // Check each number key (1 to 0) and change the scene if a valid scene name exists
            for (int i = 0; i < 10; i++)
            {
                Keys key = GetKeyForIndex(i);

                if (keyboardState.IsKeyReleased(key))
                {
                    if (!string.IsNullOrWhiteSpace(Scenes[i])) // Ensure the scene name is valid
                    {
                        NullEngine.Log.Info($"Switching to scene '{Scenes[i]}' triggered by key '{key}'.");
                        try
                        {
                            SceneManager.SetActiveScene(Scenes[i]);
                            NullEngine.Log.Debug($"Successfully switched to scene '{Scenes[i]}'.");
                        }
                        catch (Exception ex)
                        {
                            NullEngine.Log.Error($"Failed to switch to scene '{Scenes[i]}': {ex.Message}");
                        }
                    }
                    else
                    {
                        NullEngine.Log.Warn($"No valid scene configured for key '{key}'.");
                    }
                }
            }
        }

        public void HandleMouseInput(BaseMesh mesh, MouseState mouseState, Vector2 delta, bool isPressed)
        {
            // This component does not handle mouse input
        }

        public void Update(BaseMesh mesh, float deltaTime)
        {
            // This component does not perform updates
        }

        private Keys GetKeyForIndex(int index)
        {
            // Map index to number keys (1 to 0, index 0 corresponds to 'D1', index 9 corresponds to 'D0')
            return index switch
            {
                0 => Keys.D1,
                1 => Keys.D2,
                2 => Keys.D3,
                3 => Keys.D4,
                4 => Keys.D5,
                5 => Keys.D6,
                6 => Keys.D7,
                7 => Keys.D8,
                8 => Keys.D9,
                9 => Keys.D0,
                _ => throw new ArgumentOutOfRangeException(nameof(index), "Index must be between 0 and 9."),
            };
        }
    }
}
