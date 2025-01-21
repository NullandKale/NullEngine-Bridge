using NullEngine;

namespace RGBDRenderer
{
    // Entry point of our RGBDRenderer project
    internal class Program
    {
        static void Main(string[] args)
        {
            // First we initialize the Log singleton (from NullEngine)
            // This sets up logging functionality and the directory where log files will be stored
            Log.Initialize("logs/");

            // We then create a new instance of the RGBDWindow (defined below) inside a using block,
            // which ensures proper cleanup of resources once the window is closed.
            using (var window = new RGBDWindow())
            {
                // This starts the run loop of RGBDWindow and blocks until the window is closed.
                window.Run();
            }
        }
    }

    // The RGBDWindow class extends the MainWindow class from NullEngine
    // MainWindow handles the boilerplate for OpenGL, the Bridge SDK, and loading scenes from disk
    public class RGBDWindow : MainWindow
    {
        // Override GetScenes() so we can specify which scene definitions (JSON) and scene names to load
        // In this case, there's only one file: "Assets/Scenes/RGBDScene.json"
        // and one scene inside it called "RGBDScene0".
        protected override (string SceneFilePath, string ActiveSceneName)[] GetScenes()
        {
            return new[]
            {
                ("Assets/Scenes/RGBDScene.json", "RGBDScene0"),
            };
        }

        // Override GetSceneIndex() to choose which scene from our list in GetScenes to load by default
        // We only have one scene, so return 0
        protected override int GetSceneIndex()
        {
            return 0;
        }

    }
}
