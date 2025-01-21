using NullEngine;

namespace Waves
{
    public class WaveWindow : MainWindow
    {
        // Virtual methods to provide scenes and scene index
        protected override (string SceneFilePath, string ActiveSceneName)[] GetScenes()
        {
            // Default scenes
            return new[]
            {
                ("Assets/Scenes/WavesScene.json", "WavesScene0"),
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

            using (var window = new WaveWindow())
            {
                window.Run();
            }
        }
    }
}
