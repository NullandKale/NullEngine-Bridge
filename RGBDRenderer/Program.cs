using NullEngine;

namespace RGBDRenderer
{
    public class RGBDWindow : MainWindow
    {
        // Virtual methods to provide scenes and scene index
        protected override (string SceneFilePath, string ActiveSceneName)[] GetScenes()
        {
            // Default scenes
            return new[]
            {
                ("Assets/Scenes/RGBDScene.json", "RGBDScene0"),
            };
        }

        protected override int GetSceneIndex()
        {
            // Default scene index
            return 0;
        }

    }

    internal class Program
    {
        static void Main(string[] args)
        {
            Log.Initialize("logs/");

            using (var window = new RGBDWindow())
            {
                window.Run();
            }
        }
    }
}
