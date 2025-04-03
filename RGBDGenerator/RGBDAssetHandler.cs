// Import GPU-related functionalities for parallel computing and image processing.
using GPU; // Leverage GPU resources for heavy computations
using LKG_NVIDIA_RAYS.Utils; // Include utilities specific to NVIDIA ray tracing implementations
using NullEngine.Renderer.Textures; // Texture classes for dynamic texture creation and binding
using NullEngine.Utils; // General utility functions from the NullEngine codebase
using NullEngine.Video;
using OpenTK.Windowing.Common; // For events like FileDropEventArgs
using System.Diagnostics; // Diagnostics utilities for performance measurement (e.g., Stopwatch)
using System.Drawing; // Provides basic graphics functionality, e.g., Bitmap class for image processing

namespace RGBDGenerator
{
    /// <summary>
    /// Handles loading and processing assets (images, video, camera streams) for the RGBDComponent class.
    /// Manages depth generation (via ONNX model), video/camera reading, and texture creation.
    /// This class isolates asset- and depth-related logic from the main component's rendering and input code.
    /// 
    /// Enhancement: If we detect that the user is requesting to load the same static image filename
    /// as was previously processed, we will skip reprocessing to avoid unnecessary computation.
    /// </summary>
    public class RGBDAssetHandler
    {
        // The file path for image-based input; used only when not using live video or camera.
        public string Filename;

        // Indicates if the current file stream should be interpreted as a video (based on its extension).
        public bool isVideo = false;

        // Flags that a live camera feed is active instead of a file stream.
        public bool usingCamera = false;

        // Dedicated flag for toggling recording in camera mode or video mode.
        public bool cameraRecording = false;

        // Indicates that the video file has reached its end, so further frame processing is suspended.
        public bool videoFinished = false;

        // Instead of separate video and camera readers, use the common interface.
        private IFrameReader frameReader = null;

        // When recording is enabled, this writes each processed frame to disk.
        private VideoWriter videoWriter = null;

        // Temporarily stores a GPUImage instance to avoid repeated allocations for video frame conversion.
        private GPUImage tempVideoFrame;

        // A flag to control saving of the first image conversion, possibly to prevent overwriting or redundant saves.
        private bool skipFirstSave = true;

        // The ONNX-based depth inference pipeline on the GPU.
        private DepthGenerator depthGenerator;

        // Constants representing different inference resolutions for different input modes.
        private const int imageInferenceSize = 1024;          // Resolution used for static images
        private const int videoRealTimeInferenceSize = 518;     // Lower resolution for real-time video
        private const int videoRecordInferenceSize = 518;      // Higher resolution for recording from file inputs

        // Keeps track of the last processed static image file, so we can skip reprocessing if it doesn't change.
        private string lastProcessedFilename;
        private Texture lastProcessedTexture;

        public int loadedCount = 0;

        /// <summary>
        /// Constructs the asset handler by initializing the DepthGenerator
        /// with a default model and image inference size. Adjustments to the
        /// inference size will be applied dynamically based on input type.
        /// </summary>
        public RGBDAssetHandler(string Filename)
        {
            this.Filename = Filename;
            // By default, use the high-res setting for static images.
            int inferenceSize = imageInferenceSize;
            // Initialize the depth generator using a specific ONNX model.
            depthGenerator = new DepthGenerator(inferenceSize, "Assets/depth-anything-v2-small.onnx");
        }

        /// <summary>
        /// Subscribes to the FileDrop event so we can intercept file paths
        /// dropped onto the application window. This sets isVideo or usingCamera
        /// as needed and updates inference size in response.
        /// </summary>
        /// <param name="window">The primary application window.</param>
        public void SubscribeToFileDrop(OpenTK.Windowing.Desktop.NativeWindow window)
        {
            window.FileDrop += FileDrop;
        }

        /// <summary>
        /// The FileDrop event handler: captures file paths dropped onto the application window
        /// and updates internal flags for video or image loading.
        /// </summary>
        /// <param name="obj">FileDropEventArgs containing the dropped filenames.</param>
        private void FileDrop(FileDropEventArgs obj)
        {
            if (obj.FileNames.Length > 0 && System.IO.File.Exists(obj.FileNames[0]))
            {
                DisposeCurrentStream();

                Filename = obj.FileNames[0];
                string ext = System.IO.Path.GetExtension(Filename).ToLower();
                isVideo = (ext == ".mp4" || ext == ".avi" || ext == ".mov" || ext == ".mkv");
                usingCamera = false;
                videoFinished = false;
                UpdateDepthInferenceSize();
                loadedCount++;
            }
        }

        /// <summary>
        /// Chooses an appropriate inference size based on whether we are using
        /// a camera feed, a video, or a static image. If recording, a higher
        /// resolution is used for better output quality.
        /// </summary>
        public void UpdateDepthInferenceSize()
        {
            int newInferenceSize;
            if (usingCamera)
            {
                newInferenceSize = videoRealTimeInferenceSize; // Real-time for live camera
            }
            else if (isVideo)
            {
                // Video: if recording is active, bump resolution; otherwise real-time size
                newInferenceSize = (videoWriter != null ? videoRecordInferenceSize : videoRealTimeInferenceSize);
            }
            else
            {
                newInferenceSize = imageInferenceSize; // High resolution for static images
            }
            depthGenerator.UpdateInferenceSize(newInferenceSize);
        }

        /// <summary>
        /// Safely disposes of any active video, camera, or writing streams.
        /// This prevents resource leaks when switching input sources.
        /// </summary>
        public void DisposeCurrentStream()
        {
            if (frameReader != null)
            {
                frameReader.Stop();
                frameReader.Dispose();
                frameReader = null;
            }
            if (videoWriter != null)
            {
                videoWriter.Dispose();
                videoWriter = null;
            }
        }

        /// <summary>
        /// Loads a static image, runs depth inference on it, and returns a GPU texture with RGBD data.
        /// Optionally skips saving the first result to disk to avoid duplicates (useful for repeated conversions).
        /// 
        /// If the filename is the same as the last processed static image, this method will return the previously
        /// generated texture rather than reprocessing the file.
        /// </summary>
        /// <param name="skipSave">If true, doesn't write out the resulting RGBD to disk.</param>
        /// <returns>A texture with composite RGBD data, or null on failure.</returns>
        public Texture LoadTextureFromImage(bool skipSave)
        {
            // If this is the same static image as last time, return the previously processed texture
            if (lastProcessedFilename == Filename && lastProcessedTexture != null)
            {
                return null;
            }

            if (!System.IO.File.Exists(Filename)) return null;

            // Attempt to load the image into a GPUImage and compute depth
            if (GPUImage.TryLoad(Filename, out GPUImage loadedImage))
            {
                Stopwatch stopwatch = Stopwatch.StartNew();
                GPUImage output = depthGenerator.ComputeDepth(loadedImage, 0.1f, true);
                stopwatch.Stop();

                loadedImage.Dispose();
                Bitmap result = output.GetBitmap(); // Convert GPU data to a Bitmap

                // Conditionally save the resulting RGBD image
                if (!skipSave)
                {
                    string outputFileName = "output_" +
                        System.IO.Path.GetFileNameWithoutExtension(Filename) + "_" +
                        System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".png";
                    result.Save(outputFileName, System.Drawing.Imaging.ImageFormat.Png);
                }

                // Convert the Bitmap back to a texture
                Texture tex = CreateTextureFromBitmap(result);
                result.Dispose();
                output.Dispose();

                // Cache the results so we don't reprocess the same file next time
                lastProcessedFilename = Filename;
                lastProcessedTexture = tex;

                return tex;
            }
            return null;
        }

        public void SaveScreenshot()
        {
            if (depthGenerator.historyFrame == null)
            {
                Console.WriteLine("No final RGBD image available to save. 'historyFrame' is null.");
                return;
            }

            string baseName = System.IO.Path.GetFileNameWithoutExtension(Filename);
            string outName = $"screenshot_{baseName}_{System.DateTime.Now:yyyyMMdd_HHmmss}.png";

            depthGenerator.historyFrame.GetBitmap().Save(outName);
        }

        /// <summary>
        /// Converts raw video/camera frame data to an RGBD image via the DepthGenerator and returns GPUImage data.
        /// </summary>
        /// <param name="width">Frame width.</param>
        /// <param name="height">Frame height.</param>
        /// <param name="rgbaData">Pointer to the frame data in RGBA format.</param>
        /// <returns>A GPUImage containing combined RGBD data.</returns>
        private GPUImage GenerateRGBDImageFromRGBBitmap(int width, int height, System.IntPtr rgbaData)
        {
            // Reuse or create a new GPUImage buffer to avoid overhead
            if (tempVideoFrame == null || tempVideoFrame.width != width || tempVideoFrame.height != height)
            {
                tempVideoFrame = new GPUImage(width, height, rgbaData);
            }
            else
            {
                // Directly copy the new frame data into the existing GPUImage buffer
                tempVideoFrame.fromCPU_UNSAFE(rgbaData);
            }
            // The second parameter (cameraRecording) can adjust internal filters in some depth generator pipelines
            GPUImage output = depthGenerator.ComputeDepth(tempVideoFrame, 0.1f, cameraRecording);
            output.toCPU(); // Transfer to CPU memory for subsequent operations
            return output;
        }

        /// <summary>
        /// Creates a GPU texture from a System.Drawing.Bitmap by locking the bits
        /// and copying them into the GPU.
        /// </summary>
        private Texture CreateTextureFromBitmap(Bitmap bmp)
        {
            Rectangle rect = new Rectangle(0, 0, bmp.Width, bmp.Height);
            var bmpData = bmp.LockBits(rect, System.Drawing.Imaging.ImageLockMode.ReadOnly, bmp.PixelFormat);
            Texture tex = new Texture("RGBD", bmpData.Scan0, bmp.Width, bmp.Height, false);
            bmp.UnlockBits(bmpData);
            return tex;
        }

        /// <summary>
        /// Updates video or camera streams by grabbing the next frame, running depth inference,
        /// and producing a new texture. If recording is active, frames are also written to disk.
        /// Only relevant if isVideo==true or usingCamera==true.
        /// </summary>
        /// <returns>A newly created Texture with RGBD information, or null if no frame is available.</returns>
        public Texture UpdateVideoFrame()
        {
            if (videoFinished && !usingCamera) return null;

            // If we haven't yet opened a reader for a video file, do so.
            if (!usingCamera && frameReader == null && System.IO.File.Exists(Filename))
            {
                frameReader = new AsyncFfmpegVideoReader(Filename, false, true);
            }

            // Signal the reader to advance to the next frame.
            if (frameReader != null)
            {
                frameReader.PopFrame();
            }
            else
            {
                return null; // No valid frame source.
            }

            // Acquire the frame data pointer and dimensions.
            System.IntPtr framePtr = frameReader.GetCurrentFramePtr();
            int width = frameReader.Width;
            int height = frameReader.Height;

            // Convert the raw RGB frame to an RGBD GPUImage.
            GPUImage rgbdFrame = GenerateRGBDImageFromRGBBitmap(width, height, framePtr);

            // Write out the frame if we're recording.
            if (videoWriter != null)
            {
                videoWriter.WriteFrame(rgbdFrame.data);
            }

            // Create a new GPU Texture from the data.
            Texture newTexture = new Texture("RGBD", rgbdFrame.data, rgbdFrame.width, rgbdFrame.height, !usingCamera);
            rgbdFrame.Dispose();

            // If we've looped the video during recording, reinitialize the video reader.
            if (!usingCamera && videoWriter != null && frameReader != null && frameReader.HasLooped)
            {
                videoWriter.Dispose();
                videoWriter = null;

                frameReader.Stop();
                frameReader.Dispose();
                frameReader = new AsyncFfmpegVideoReader(Filename, false, true);
                UpdateDepthInferenceSize();
            }

            return newTexture;
        }

        /// <summary>
        /// Loads a static image or updates a live/camera feed. Returns the appropriate
        /// texture (RGBD composite) or null if nothing is available.
        /// Also controls skipping the first image save if requested.
        /// 
        /// For static images, if the filename is the same as the last one processed,
        /// we won't reprocess it. For videos, we always update frames normally.
        /// </summary>
        public Texture GetLatestTexture()
        {
            // For camera or video input, update from the frame reader.
            if (isVideo || usingCamera)
            {
                return UpdateVideoFrame();
            }
            else
            {
                // Static image logic.
                Texture tex = LoadTextureFromImage(skipFirstSave);
                if (skipFirstSave)
                    skipFirstSave = false;
                return tex;
            }
        }

        /// <summary>
        /// Opens a camera stream from the specified device index. Disposes
        /// any existing streams to avoid conflicts, then updates inference size.
        /// </summary>
        /// <param name="cameraIndex">Zero-based camera index to open.</param>
        public void OpenCamera(int cameraIndex)
        {
            DisposeCurrentStream();

            try
            {
                frameReader = new AsyncCameraReader(cameraIndex, 1.77f, true, true);
            }
            catch (System.Exception ex)
            {
                Console.WriteLine(ex.Message);
                return;
            }

            usingCamera = true;
            videoFinished = false;
            UpdateDepthInferenceSize();
        }

        /// <summary>
        /// Toggles recording. For camera input, starts or stops writing. For video input, reopens the
        /// reader in a recording-friendly mode. Uses file naming based on timestamps or existing filenames.
        /// </summary>
        public void ToggleRecording()
        {
            // Live camera scenario.
            if (usingCamera && frameReader != null)
            {
                cameraRecording = !cameraRecording;
                if (cameraRecording)
                {
                    string outputFilename = "output_camera_" + System.DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".mp4";
                    videoWriter = new VideoWriter(outputFilename, frameReader.Fps, frameReader.Width * 2, frameReader.Height);
                }
                else
                {
                    videoWriter.Dispose();
                    videoWriter = null;
                }
                UpdateDepthInferenceSize();
            }
            // Video file scenario.
            else if (!usingCamera && frameReader != null)
            {
                frameReader.Stop();
                frameReader.Dispose();
                // Reopen in recording mode.
                frameReader = new AsyncFfmpegVideoReader(Filename, true, true, false);

                if (videoWriter != null)
                {
                    videoWriter.Dispose();
                    videoWriter = null;
                }
                else
                {
                    string outputFilename = "output_" + System.IO.Path.GetFileName(Filename);
                    videoWriter = new VideoWriter(outputFilename, frameReader.Fps, frameReader.Width * 2, frameReader.Height);
                }
                UpdateDepthInferenceSize();
            }
        }
    }
}
