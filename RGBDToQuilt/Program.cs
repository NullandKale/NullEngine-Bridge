using System;
using System.IO;
using System.Linq;
using BridgeSDK;
using NullEngine;
using NullEngine.Utils;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;

namespace RGBDToQuiltCLI
{
    class Program
    {
        static void Main(string[] args)
        {
            Log.Initialize("logs/");
            // use Log.Debug and Log.Error instead of the console

            if (args.Length == 0 || args.Contains("--help"))
            {
                PrintHelp();
                return;
            }

            try
            {
                // Parse command-line arguments
                var parsedArgs = ParseArguments(args);

                // Validate input file
                if (!File.Exists(parsedArgs.InputPath))
                {
                    throw new FileNotFoundException($"Input file not found: {parsedArgs.InputPath}");
                }

                // Validate depth inversion
                if (parsedArgs.DepthInversion != 0 && parsedArgs.DepthInversion != 1)
                {
                    throw new ArgumentException("Depth inversion must be 0 or 1.");
                }

                // Validate output directory
                string outputDirectory = Path.GetDirectoryName(parsedArgs.OutputPath);
                if (!Directory.Exists(outputDirectory))
                {
                    throw new DirectoryNotFoundException($"Output directory does not exist: {outputDirectory}");
                }

                // Create an offscreen OpenGL context using OpenTK 4.x
                using var window = CreateOffscreenOpenGLContext();
                window.MakeCurrent(); // Make the context current

                // Initialize the Bridge SDK
                if (!Controller.Initialize("RGBDToQuiltCLI"))
                {
                    Console.Error.WriteLine("Failed to initialize the Bridge SDK. Ensure the SDK is installed and accessible.");
                    return;
                }

                List<DisplayInfo> displays = Controller.GetDisplayInfoList();
                Window wnd = 0;
                BridgeWindowData bridgeData;

                if (displays.Count > 0 && Controller.InstanceOffscreenWindowGL(ref wnd))
                {
                    bridgeData = Controller.GetWindowData(wnd);
                }
                else
                {
                    Log.Debug("No display connected");
                    return;
                }

                // Log progress
                Console.WriteLine($"Processing input file: {parsedArgs.InputPath}");
                Console.WriteLine($"Generating quilt with {parsedArgs.Columns}x{parsedArgs.Rows} views...");
                Console.WriteLine($"Output will be saved to: {parsedArgs.OutputPath}");

                // Call the QuiltifyRGBD function
                bool success = Controller.QuiltifyRGBD(
                    wnd,
                    parsedArgs.Columns,
                    parsedArgs.Rows,
                    parsedArgs.Views,
                    parsedArgs.Aspect,
                    parsedArgs.Zoom,
                    0, // cam_dist (ignored)
                    0, // fov (ignored)
                    parsedArgs.CropPosX,
                    parsedArgs.CropPosY,
                    parsedArgs.DepthInversion,
                    0, // chroma_depth (ignored)
                    parsedArgs.DepthLoc,
                    parsedArgs.Depthiness,
                    0, // depth_cutoff (ignored)
                    parsedArgs.Focus,
                    parsedArgs.InputPath,
                    parsedArgs.OutputPath
                );

                if (success)
                {
                    Console.WriteLine("Quilt image successfully generated.");
                }
                else
                {
                    Console.Error.WriteLine("Failed to generate quilt image.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
            finally
            {
                // Clean up the Bridge SDK
                Controller.Uninitialize();
            }
        }

        private static NativeWindow CreateOffscreenOpenGLContext()
        {
            // Create a NativeWindow with a size of 1x1 (offscreen)
            NativeWindowSettings settings = NativeWindowSettings.Default;
            settings.Size = new OpenTK.Mathematics.Vector2i(1, 1);
            settings.WindowBorder = WindowBorder.Hidden;
            settings.WindowState = WindowState.Minimized;
            settings.StartVisible = false; // Ensure the window is not visible

            return new NativeWindow(settings);
        }

        private static void PrintHelp()
        {
            Console.WriteLine("RGBD to Quilt Image Converter");
            Console.WriteLine("Usage: RGBDToQuiltCLI [options]");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --columns <value>        Number of columns in the quilt (default: 10)");
            Console.WriteLine("  --rows <value>           Number of rows in the quilt (default: 10)");
            Console.WriteLine("  --views <value>          Number of views in the quilt (default: 100)");
            Console.WriteLine("  --aspect <value>         Aspect ratio of the quilt (default: -1 (use input image aspect ratio))");
            Console.WriteLine("  --zoom <value>           Zoom factor (default: 0)");
            Console.WriteLine("  --crop-pos-x <value>     Horizontal crop position (default: 0)");
            Console.WriteLine("  --crop-pos-y <value>     Vertical crop position (default: 0)");
            Console.WriteLine("  --depth-inversion <value> Depth inversion flag (0 or 1, default: 0)");
            Console.WriteLine("  --depth-loc <value>      Depth location (default: 2)"); // bottom: 0, top: 1, left: 2, right: 3
            Console.WriteLine("  --depthiness <value>     Depthiness factor (default: 0)");
            Console.WriteLine("  --focus <value>          Focus factor (default: 0)");
            Console.WriteLine("  --input <path>           Path to the input RGB+D image (required)");
            Console.WriteLine("  --output <path>          Path to save the output quilt image (optional)");
            Console.WriteLine("  --help                   Show this help message");
        }

        private static ParsedArguments ParseArguments(string[] args)
        {
            var parsedArgs = new ParsedArguments
            {
                Columns = 10, // Default columns
                Rows = 10,    // Default rows
                Views = 100,  // Default views
                Aspect = -1,  // Default aspect ratio (use input image aspect ratio)
                Zoom = 0,     // Default zoom
                CropPosX = 0, // Default crop position X
                CropPosY = 0, // Default crop position Y
                DepthInversion = 0, // Default depth inversion
                DepthLoc = 2, // Default depth location
                Depthiness = 0, // Default depthiness
                Focus = 0,    // Default focus
            };

            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--columns":
                        parsedArgs.Columns = ulong.Parse(args[++i]);
                        break;
                    case "--rows":
                        parsedArgs.Rows = ulong.Parse(args[++i]);
                        break;
                    case "--views":
                        parsedArgs.Views = ulong.Parse(args[++i]);
                        break;
                    case "--aspect":
                        parsedArgs.Aspect = float.Parse(args[++i]);
                        break;
                    case "--zoom":
                        parsedArgs.Zoom = float.Parse(args[++i]);
                        break;
                    case "--crop-pos-x":
                        parsedArgs.CropPosX = float.Parse(args[++i]);
                        break;
                    case "--crop-pos-y":
                        parsedArgs.CropPosY = float.Parse(args[++i]);
                        break;
                    case "--depth-inversion":
                        parsedArgs.DepthInversion = ulong.Parse(args[++i]);
                        break;
                    case "--depth-loc":
                        parsedArgs.DepthLoc = ulong.Parse(args[++i]);
                        break;
                    case "--depthiness":
                        parsedArgs.Depthiness = float.Parse(args[++i]);
                        break;
                    case "--focus":
                        parsedArgs.Focus = float.Parse(args[++i]);
                        break;
                    case "--input":
                        parsedArgs.InputPath = args[++i];
                        break;
                    case "--output":
                        parsedArgs.OutputPath = args[++i];
                        break;
                    default:
                        throw new ArgumentException($"Unknown argument: {args[i]}");
                }
            }

            // Validate required input path
            if (string.IsNullOrEmpty(parsedArgs.InputPath))
            {
                throw new ArgumentException("Input path is required. Use --help for usage information.");
            }

            // Generate output path if not provided
            if (string.IsNullOrEmpty(parsedArgs.OutputPath))
            {
                string inputFileName = Path.GetFileNameWithoutExtension(parsedArgs.InputPath);
                string outputFileName = $"{inputFileName}_qs{parsedArgs.Columns}x{parsedArgs.Rows}a{parsedArgs.Aspect}.png";
                parsedArgs.OutputPath = Path.Combine(Path.GetDirectoryName(parsedArgs.InputPath), outputFileName);
            }

            return parsedArgs;
        }

        private class ParsedArguments
        {
            public ulong Columns { get; set; }
            public ulong Rows { get; set; }
            public ulong Views { get; set; }
            public float Aspect { get; set; }
            public float Zoom { get; set; }
            public float CropPosX { get; set; } = 0;
            public float CropPosY { get; set; } = 0;
            public ulong DepthInversion { get; set; } = 0;
            public ulong DepthLoc { get; set; } = 0;
            public float Depthiness { get; set; } = 0;
            public float Focus { get; set; } = 0;
            public string InputPath { get; set; }
            public string OutputPath { get; set; }
        }
    }
}
