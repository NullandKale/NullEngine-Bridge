using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Renderer.Scenes
{
    public static class SceneManager
    {
        private static Dictionary<string, Scene> scenes = new Dictionary<string, Scene>();
        private static Scene activeScene;

        public static void AddScene(string name, Scene scene)
        {
            if (scenes.ContainsKey(name))
            {
                throw new Exception($"Scene with name '{name}' already exists.");
            }

            scenes[name] = scene;
        }

        public static void RemoveScene(string name)
        {
            if (scenes.ContainsKey(name))
            {
                scenes.Remove(name);
            }
        }

        public static void SetActiveScene(string name)
        {
            if (scenes.TryGetValue(name, out var scene))
            {
                activeScene = scene;
            }
            else
            {
                throw new Exception($"Scene with name '{name}' not found.");
            }
        }

        public static Scene GetActiveScene()
        {
            return activeScene;
        }
    }

}
