# RGBD to Quilt Converter

The RGBD to Quilt converter is a command-line tool that transforms RGB+D images into quilt images using the Bridge SDK.

## Command-Line Arguments and Usage

The program accepts these arguments:

```bash
--columns <value>        Number of columns in the quilt (default: 10)
--rows <value>           Number of rows in the quilt (default: 10)
--views <value>          Number of views in the quilt (default: columns * rows)
--aspect <value>         Aspect ratio of the quilt (default: -1, use input image aspect ratio)
--depth-inversion <value> Depth inversion flag (0 or 1, default: 0)
--depth-loc <value>      Depth location (0: Bottom, 1: Top, 2: Left, 3: Right, default: 2)
--depthiness <value>     Depthiness factor (default: 1, range: 0 to 2)
--input <path>           Path to the input RGB+D image (required)
--output <path>          Path to save the output quilt image (optional)
```

While all these arguments are available, the tool only requires a single argument - the input image:

```bash
RGBDToQuiltCLI.exe --input input.png
```

This outputs the quilt as input_qs10x10a1.78125.png with the default settings.

## How does the code work?

The RGBD to Quilt converter uses the Bridge SDK to do the conversion, so we need an OpenGL context. Here is how we set that up:

### 1. Creating OpenGL Context

We use OpenTK for the OpenGL bindings in C#, to create an OpenGL context and a offscreen window. For now all you need to worry about is that this will allow us to initialize the Bridge SDK. This is necessary because the Bridge SDK requires an OpenGL context to function, even though we're running a command-line tool.

```csharp
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
```

### 2. Initializing Bridge SDK

After creating the OpenGL context, we initialize the Bridge SDK:

```csharp
// Create an offscreen OpenGL context for bridge and make the context current
using var window = CreateOffscreenOpenGLContext();
window.MakeCurrent();

// Initialize the Bridge SDK
if (!Controller.Initialize("RGBDToQuiltCLI"))
{
    Console.Error.WriteLine("Failed to initialize the Bridge SDK. Ensure the SDK is installed and accessible.");
    return;
}
```

### 3. Processing the Image

The Bridge SDK provides several key components for processing:
- **Display Info**: Gets information about connected displays
- **Window Data**: Stores settings and state for the Bridge window

Next we initialize the Bridge SDK, get a list of the displays bridge can see, and activate an offscreen bridge window. This will contain all the settings of the connected display, but it will not appear anywhere. Note it is possible to do this without a display connected, but I left that as an exercise for the reader.

```csharp
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
```

Now we just call the helper function in the bridge SDK:

```csharp
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

```

