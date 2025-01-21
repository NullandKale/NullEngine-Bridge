using NullEngine;

namespace Tutorial01_RGBD
{
    public class RGBDWindow : MainWindow
    {
        protected override (string SceneFilePath, string ActiveSceneName)[] GetScenes()
        {
            // Default scenes
            return new[]
            {
                ("Assets/Scenes/TestScene.json", "TestScene0"),
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
