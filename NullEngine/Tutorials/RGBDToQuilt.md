# RGBD to Quilt

One features of the bridge SDK is the ability to use the bridge RGBD to Quilt converter, but this bakes in the focus and depthiness settings, so previewing the settings is super important. 

You can see the function in the BridgeSDK.cs file here:

```csharp
public static bool QuiltifyRGBD(Window wnd, ulong columns, ulong rows, ulong views, float aspect, float zoom, float cam_dist, float fov, float crop_pos_x, float crop_pos_y, ulong depth_inversion, ulong chroma_depth, ulong depth_loc, float depthiness, float depth_cutoff, float focus, string input_path, string output_path)
{
    try
    {
        var func = DynamicLibraryLoader.LoadFunction<QuiltifyRGBDDelegate>(libraryPath, "quiltify_rgbd");
        return func(wnd, columns, rows, views, aspect, zoom, cam_dist, fov, crop_pos_x, crop_pos_y, depth_inversion, chroma_depth, depth_loc, depthiness, depth_cutoff, focus, input_path, output_path);
    }
    catch (Exception ex)
    {
        Log.Debug("Error: " + ex.Message);
        return false;
    }
}
```