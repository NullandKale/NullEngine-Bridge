using System;
using System.IO;
using System.Linq;
using BridgeSDK;
using NullEngine;
using NullEngine.Utils;
using OpenTK.Windowing.Common;
using OpenTK.Windowing.Desktop;
using System.Drawing; // Add this for image processing

namespace RGBDToQuiltCLI
{
    class Program
    {
        static void Main(string[] args)
        {
            Log.Initialize("logs/");

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

                // Create an offscreen OpenGL context for bridge and make the context current
                using var window = CreateOffscreenOpenGLContext();
                window.MakeCurrent();

                // Initialize the Bridge SDK
                if (!Controller.Initialize("RGBDToQuiltCLI"))
                {
                    Console.Error.WriteLine("Failed to initialize the Bridge SDK. Ensure the SDK is installed and accessible.");
                    return;
                }

                // Get a list of all the displays connected
                List<DisplayInfo> displays = Controller.GetDisplayInfoList();
                Window wnd = 0;
                BridgeWindowData bridgeData;

                // use this function if you want to instance a non offscreen window
                // if (displays.Count > 0 && Controller.InstanceWindowGL(ref wnd, displays[0].DisplayId))

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
                    1, // zoom (always 1)
                    0, // cam_dist (ignored)
                    0, // fov (ignored)
                    0, // crop_pos_x (always 0)
                    0, // crop_pos_y (always 0)
                    parsedArgs.DepthInversion,
                    0, // chroma_depth (ignored)
                    parsedArgs.DepthLoc,
                    parsedArgs.Depthiness,
                    1, // depth_cutoff (ignored)
                    0, // focus (always 0)
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
            Console.WriteLine("  --views <value>          Number of views in the quilt (default: columns * rows)");
            Console.WriteLine("  --aspect <value>         Aspect ratio of the quilt (default: -1 (use input image aspect ratio))");
            Console.WriteLine("  --depth-inversion <value> Depth inversion flag (0 or 1, default: 0).");
            Console.WriteLine("                             • 0: White is close, black is far.");
            Console.WriteLine("                             • 1: Black is close, white is far.");
            Console.WriteLine("  --depth-loc <value>      Depth location (default: 2). Valid values:");
            Console.WriteLine("                             • 0: Bottom");
            Console.WriteLine("                             • 1: Top");
            Console.WriteLine("                             • 2: Left");
            Console.WriteLine("                             • 3: Right");
            Console.WriteLine("  --depthiness <value>     Depthiness factor (default: 1). Range: 0 to 2.");
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
                Views = 0,    // Default views (0 means use columns * rows)
                Aspect = -1,  // Default aspect ratio (use input image aspect ratio)
                DepthInversion = 0, // Default depth inversion
                DepthLoc = 2, // Default depth location
                Depthiness = 1, // Default depthiness
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
                    case "--depth-inversion":
                        parsedArgs.DepthInversion = ulong.Parse(args[++i]);
                        break;
                    case "--depth-loc":
                        parsedArgs.DepthLoc = ulong.Parse(args[++i]);
                        break;
                    case "--depthiness":
                        parsedArgs.Depthiness = float.Parse(args[++i]);
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

            // Calculate views if not provided
            if (parsedArgs.Views == 0)
            {
                parsedArgs.Views = parsedArgs.Columns * parsedArgs.Rows;
            }

            // Calculate aspect ratio if it is -1
            if (parsedArgs.Aspect == -1)
            {
                parsedArgs.Aspect = CalculateAspectRatio(parsedArgs.InputPath, parsedArgs.DepthLoc);
                Console.WriteLine($"Calculated aspect ratio: {parsedArgs.Aspect}");
            }

            // Generate output path if not provided
            if (string.IsNullOrEmpty(parsedArgs.OutputPath))
            {
                string inputFileName = Path.GetFileNameWithoutExtension(parsedArgs.InputPath);
                string outputFileName = $"{inputFileName}_qs{parsedArgs.Columns}x{parsedArgs.Rows}a{parsedArgs.Aspect}.png";

                // Get the directory of the input file
                string inputDirectory = Path.GetDirectoryName(parsedArgs.InputPath);

                // If the input path is just a filename (no directory), use the current working directory
                if (string.IsNullOrEmpty(inputDirectory))
                {
                    inputDirectory = Directory.GetCurrentDirectory();
                }

                parsedArgs.OutputPath = Path.Combine(inputDirectory, outputFileName);
            }

            return parsedArgs;
        }
        private static float CalculateAspectRatio(string imagePath, ulong depthLoc)
        {
            try
            {
                // Load the image using System.Drawing
                using (var image = Image.FromFile(imagePath))
                {
                    float width = image.Width;
                    float height = image.Height;

                    // Adjust the aspect ratio based on depth location
                    switch (depthLoc)
                    {
                        case 0: // Bottom
                        case 1: // Top
                                // Depth is on top or bottom, so the color part is half the height
                            height /= 2;
                            break;
                        case 2: // Left
                        case 3: // Right
                                // Depth is on left or right, so the color part is half the width
                            width /= 2;
                            break;
                        default:
                            throw new ArgumentException("Invalid depth location. Valid values are 0 (bottom), 1 (top), 2 (left), or 3 (right).");
                    }

                    // Calculate aspect ratio (width / height)
                    return width / height;
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error calculating aspect ratio: {ex.Message}");
                throw new ArgumentException("Failed to calculate aspect ratio. Ensure the input file is a valid image.");
            }
        }

        private class ParsedArguments
        {
            public ulong Columns { get; set; }
            public ulong Rows { get; set; }
            public ulong Views { get; set; }
            public float Aspect { get; set; }
            public ulong DepthInversion { get; set; }
            public ulong DepthLoc { get; set; }
            public float Depthiness { get; set; }
            public string InputPath { get; set; }
            public string OutputPath { get; set; }
        }
    }
}
