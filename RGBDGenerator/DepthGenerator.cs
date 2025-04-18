using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using GPU;
using ILGPU;
using ILGPU.Runtime;
using ILGPU.Runtime.CPU;
using ILGPU.Runtime.Cuda;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using RGBDGenerator;

namespace LKG_NVIDIA_RAYS.Utils
{
    public sealed class DepthGenerator : IDisposable
    {
        // ----------------------------------
        // FIELDS, PROPERTIES, & CONSTRUCTOR
        // ----------------------------------

        public GPUImage? historyFrame;

        // ILGPU context/device
        public Context context;
        public Accelerator device;

        // GPU kernels for color conversion, TAA, etc.
        public Action<Index1D, dImage, ArrayView<float>, int, int, float, int> imageToRGBFloatsKernel;
        public Action<Index1D, ArrayView<float>, dImage, dImage, int, int, float, float, int> depthFloatsToBGRAImageKernel;
        public Action<Index1D, dDepthRollingWindow, ArrayView<float>> filterDepthRollingWindowKernel;
        public Action<Index1D, dImage, dImage, dImage, float, int> temporalAAKernel;

        // Lanczos resize kernel + reusable tmp image for server scaling
        public Action<Index1D, dImage, dImage, float> lanczosScaleKernel;
        private GPUImage? _tmpScaledForServer;

        // ONNX session
        private readonly InferenceSession _session;

        // Input size
        private int _targetWidth;
        private int _targetHeight;

        // GPU buffers for pre/post-processing
        private MemoryBuffer1D<float, Stride1D.Dense>? inputFloatBuffer;
        private MemoryBuffer1D<float, Stride1D.Dense>? depthFloatBuffer;

        // CPU float arrays
        private float[]? inputFloatData;
        private DenseTensor<float>? inputTensor;
        private float[]? depthFloats;

        // For storing the final color output
        private GPUImage? reusableOutImage;

        // Rolling-window for depth filtering
        public DepthRollingWindow? rollingWindow;

        // Frame count for TAA
        private int frameCount = 0;

        // Auto-focus flags & parameters
        public bool AutoFocusEnabled { get; set; }
        public float AutoFocusStrength { get; set; }
        public bool AutoFocusUseFaces { get; set; }
        public float LastFocusDepth { get; set; }
        public float FocusSmoothing { get; set; }

        // For face detection
        private readonly FaceDetector? _faceDetector;
        public List<FaceBox>? LastDetectedFaces { get; private set; }

        // NEW: Our encapsulated auto-focus/analysis helper
        private DepthAutoFocusAndStats? _autoFocusAndStats;

        private float border;

        // --- Remote depth‑server support ------------------------------
        private readonly bool _useServerDepth;          // choose between ONNX vs server
        private readonly HttpClient _httpClient;             // shared for all requests
        private readonly string _depthEndpoint;          // e.g. "http://127.0.0.1:5001/depth"

        public DepthGenerator(
                int size,
                string modelPath,
                string? faceModelPath = null,
                bool useServerDepth = false,
                string depthServerBaseUrl = "http://127.0.0.1:5001")
        {
            _useServerDepth = useServerDepth;
            _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
            _depthEndpoint = $"{depthServerBaseUrl.TrimEnd('/')}/depth_raw";

            // ---------- ILGPU context/device (unchanged) -----------------
            int adjustedSize = (int)Math.Floor(size / 14.0) * 14;
            if (adjustedSize < 14) adjustedSize = 14;

            bool debug = false;
            context = Context.Create(builder => builder
                .CPU()
                .Cuda()
                .EnableAlgorithms()
                .Math(MathMode.Fast32BitOnly)
                .Inlining(InliningMode.Aggressive)
                .AutoAssertions()
                .Optimize(OptimizationLevel.O2));
            device = context.GetPreferredDevice(preferCPU: debug).CreateAccelerator(context);

            // ---------- kernels (unchanged) ------------------------------
            imageToRGBFloatsKernel = device.LoadAutoGroupedStreamKernel<Index1D, dImage, ArrayView<float>, int, int, float, int>(Kernels.ImageToRGBFloats);
            depthFloatsToBGRAImageKernel = device.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, dImage, dImage, int, int, float, float, int>(Kernels.DepthFloatsToBGRAImageFull);
            filterDepthRollingWindowKernel = device.LoadAutoGroupedStreamKernel<Index1D, dDepthRollingWindow, ArrayView<float>>(Kernels.FilterDepthRollingWindow);
            temporalAAKernel = device.LoadAutoGroupedStreamKernel<Index1D, dImage, dImage, dImage, float, int>(Kernels.TemporalAA);
            lanczosScaleKernel = device.LoadAutoGroupedStreamKernel<Index1D, dImage, dImage, float>(Kernels.ImageLanczosScale);

            // ---------- inference size + buffers -------------------------
            _targetWidth = adjustedSize;
            _targetHeight = adjustedSize;
            border = 0.0f;

            int floatCount = 3 * _targetHeight * _targetWidth;
            inputFloatData = new float[floatCount];
            inputTensor = new DenseTensor<float>(inputFloatData, new[] { 1, 3, _targetHeight, _targetWidth });
            depthFloats = new float[_targetHeight * _targetWidth];

            // ---------- ONNX setup  (skip when using server) -------------
            if (!_useServerDepth)
            {
                using var cudaProviderOptions = new OrtCUDAProviderOptions();
                var providerOptionsDict = new Dictionary<string, string>
                {
                    ["cudnn_conv_use_max_workspace"] = "1",
                    ["cudnn_conv1d_pad_to_nc1d"] = "1"
                };
                cudaProviderOptions.UpdateOptions(providerOptionsDict);

                using SessionOptions sessionOptions = SessionOptions.MakeSessionOptionWithCudaProvider(cudaProviderOptions);
                sessionOptions.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR;
                sessionOptions.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_EXTENDED;
                sessionOptions.InterOpNumThreads = 8;
                sessionOptions.IntraOpNumThreads = 8;

                _session = new InferenceSession(modelPath, sessionOptions);
            }

            // ---------- optional face‑detector & autofocus (unchanged) ---
            if (!string.IsNullOrEmpty(faceModelPath))
            {
                //_faceDetector = new FaceDetector(faceModelPath, device);
            }
            _autoFocusAndStats = new DepthAutoFocusAndStats(device);

            AutoFocusEnabled = false;
            AutoFocusStrength = 0.7f;
            AutoFocusUseFaces = false;
            LastFocusDepth = 0.5f;
            FocusSmoothing = 0.8f;
        }

        /// <summary>
        /// Sends the input frame as an uncompressed RGBA8 buffer to the
        /// Flask <code>/depth_raw</code> endpoint and receives a little‑endian
        /// <c>float32</c> depth map (values ∈ [0,1]).  
        /// The returned array length is <c>_targetWidth * _targetHeight</c>.
        /// </summary>
        private float[] RequestDepthFromServer(GPUImage inputImage, bool RGBSwapBGR /* unused now */)
        {
            // ------------------------------------------------------------------
            // A) pull RGBA8 pixels from GPUImage
            // ------------------------------------------------------------------
            int[] rgbaSrc = inputImage.toCPU();            // len = W*H, 32‑bit each
            int width = inputImage.width;              // must match _targetWidth
            int height = inputImage.height;             // must match _targetHeight

            // If you insist on fixed inference size, make sure they match here
            if (width != _targetWidth || height != _targetHeight)
                throw new InvalidOperationException(
                    $"Input dims ({width}×{height}) differ from _targetWidth/_targetHeight " +
                    $"({_targetWidth}×{_targetHeight}). Call UpdateInferenceSize first.");

            // Turn the int[] view into a byte[] without per‑pixel copying
            int byteCount = rgbaSrc.Length * sizeof(int);
            byte[] byteBuf = ArrayPool<byte>.Shared.Rent(byteCount);
            MemoryMarshal.AsBytes(rgbaSrc.AsSpan()).CopyTo(byteBuf);

            // ------------------------------------------------------------------
            // B) build multipart/form‑data
            // ------------------------------------------------------------------
            using var form = new MultipartFormDataContent
    {
        { new StringContent(width.ToString()),  "width"  },
        { new StringContent(height.ToString()), "height" }
    };

            var rawContent = new ByteArrayContent(byteBuf, 0, byteCount);
            rawContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            form.Add(rawContent, "data", "frame.raw");

            // ------------------------------------------------------------------
            // C) POST and receive depth buffer
            // ------------------------------------------------------------------
            using HttpResponseMessage resp = _httpClient.PostAsync(_depthEndpoint, form).Result;
            resp.EnsureSuccessStatusCode();
            byte[] depthBytes = resp.Content.ReadAsByteArrayAsync().Result;

            // release pooled buffer
            ArrayPool<byte>.Shared.Return(byteBuf);

            // ------------------------------------------------------------------
            // D) convert depth bytes → float[]
            // ------------------------------------------------------------------
            int expectedBytes = width * height * sizeof(float);
            if (depthBytes.Length != expectedBytes)
                throw new InvalidOperationException(
                    $"Depth buffer size mismatch (got {depthBytes.Length}, expected {expectedBytes}).");

            float[] depthArr = new float[width * height];
            Buffer.BlockCopy(depthBytes, 0, depthArr, 0, depthBytes.Length);
            return depthArr;
        }

        // ...
        // Possibly a method to let user reinit the autoFocus if needed
        // ...

        public void UpdateInferenceSize(int size)
        {
            int adjustedSize = (int)Math.Floor(size / 14.0) * 14;
            if (adjustedSize < 14)
                adjustedSize = 14;

            if (_targetWidth == adjustedSize && _targetHeight == adjustedSize)
                return;

            inputFloatBuffer?.Dispose();
            inputFloatBuffer = null;
            depthFloatBuffer?.Dispose();
            depthFloatBuffer = null;
            rollingWindow?.Dispose();
            rollingWindow = null;

            _targetWidth = adjustedSize;
            _targetHeight = adjustedSize;

            int floatCount = 3 * _targetHeight * _targetWidth;
            inputFloatData = new float[floatCount];
            inputTensor = new DenseTensor<float>(inputFloatData, new[] { 1, 3, _targetHeight, _targetWidth });
            depthFloats = new float[_targetHeight * _targetWidth];
        }

        /// <summary>
        /// Applies auto-focus logic on the CPU-side <c>depthFloats</c> array
        /// by calling our <c>DepthAutoFocusAndStats</c> helper.
        /// Updates <c>LastFocusDepth</c> with the new focus value.
        /// </summary>
        private void RunAutoFocus()
        {
            if (!AutoFocusEnabled || _autoFocusAndStats == null)
                return;

            // 1) Let our helper compute a new focus
            float newFocus = _autoFocusAndStats.ApplyAutoFocus(
                depthFloatBuffer,
                _targetWidth,
                _targetHeight,
                LastFocusDepth,
                FocusSmoothing,
                AutoFocusStrength);

            // 2) Store it for next frame
            LastFocusDepth = newFocus;

            // 3) If you want face-based refinement, you can do it on CPU here
            // or call _autoFocusAndStats.RefineFocusDepthWithFaces(...) if needed
        }

        public GPUImage ComputeDepth(GPUImage inputImage, float taa = 0.15f, bool RGBSwapBGR = false, bool detectFaces = false)
        {
            // A) Copy input image to GPU, convert to floats
            dImage inputImageGPU = inputImage.toDevice(device);

            // B) Possibly run face detection
            if (detectFaces && _faceDetector != null)
            {
                List<FaceBox> faces = _faceDetector.DetectFaces(inputImageGPU, threshold: 0.7f);
                // store or do something with faces
            }

            int totalPixels = _targetWidth * _targetHeight;

            // Ensure we have enough GPU memory for input float buffer
            if (inputFloatBuffer == null || inputFloatBuffer.Length < totalPixels * 3)
            {
                inputFloatBuffer?.Dispose();
                inputFloatBuffer = device.Allocate1D<float>(totalPixels * 3);
            }

            // GPU kernel: image => float[3 * W * H]
            imageToRGBFloatsKernel(
                totalPixels,
                inputImageGPU,
                inputFloatBuffer.View,
                _targetWidth,
                _targetHeight,
                border,
                RGBSwapBGR ? 1 : 0);
            device.Synchronize();

            // Copy GPU => CPU for ONNX inference
            inputFloatBuffer.CopyToCPU(inputFloatData);

            // -----------------------------------------------------------
            // C) Depth inference
            // -----------------------------------------------------------
            if (_useServerDepth)
            {
                // --- make sure the frame we send to the server matches the
                //     inference size (_targetWidth × _targetHeight).  If not,
                //     resize on‑GPU with Lanczos3 and reuse a temporary buffer.
                GPUImage imageForServer;

                if (inputImage.width == _targetWidth && inputImage.height == _targetHeight)
                {
                    imageForServer = inputImage;                // no scaling needed
                }
                else
                {
                    if (_tmpScaledForServer == null ||
                        _tmpScaledForServer.width != _targetWidth ||
                        _tmpScaledForServer.height != _targetHeight)
                    {
                        _tmpScaledForServer?.Dispose();
                        _tmpScaledForServer = new GPUImage(_targetWidth, _targetHeight);
                    }

                    lanczosScaleKernel(
                        _targetWidth * _targetHeight,
                        inputImage.toDevice(device),
                        _tmpScaledForServer.toDevice(device),
                        3f /* lobes: Lanczos3 */);
                    device.Synchronize();

                    imageForServer = _tmpScaledForServer;
                }

                // Send to Flask, receive float[] depth
                depthFloats = RequestDepthFromServer(imageForServer, RGBSwapBGR);
            }
            else
            {
                // ---- local ONNX path (unchanged) ----
                var container = new List<NamedOnnxValue>
    {
        NamedOnnxValue.CreateFromTensor<float>("pixel_values", inputTensor!)
    };

                using var outputs = _session.Run(container);
                var depthTensor = outputs.First().AsTensor<float>();

                ReadOnlySpan<int> dims = depthTensor.Dimensions;
                if (dims.Length != 3 || dims[1] != _targetHeight || dims[2] != _targetWidth)
                    throw new Exception("Dimension mismatch from model output.");

                unsafe
                {
                    var denseTensor = depthTensor as DenseTensor<float>;
                    if (denseTensor?.Length != depthFloats!.Length)
                        throw new InvalidOperationException("Invalid tensor format?");

                    using var srcHandle = denseTensor.Buffer.Pin();
                    fixed (float* dest = depthFloats)
                    {
                        Buffer.MemoryCopy(srcHandle.Pointer,
                                          dest,
                                          depthFloats.Length * sizeof(float),
                                          denseTensor.Length * sizeof(float));
                    }
                }
            }

            // E) Post-process with rolling window, colorize, TAA, etc.

            int outWidth = inputImage.width * 2;
            int outHeight = inputImage.height;

            // Reusable output
            if (reusableOutImage == null || reusableOutImage.width != outWidth || reusableOutImage.height != outHeight)
            {
                reusableOutImage?.Dispose();
                reusableOutImage = new GPUImage(outWidth, outHeight);
            }

            // Create or reuse history frame
            if (historyFrame == null ||
                historyFrame.width != outWidth ||
                historyFrame.height != outHeight)
            {
                historyFrame?.Dispose();
                historyFrame = new GPUImage(outWidth, outHeight);
            }

            // Copy CPU depth => GPU depth
            if (depthFloatBuffer == null || depthFloatBuffer.Length < totalPixels)
            {
                depthFloatBuffer?.Dispose();
                depthFloatBuffer = device.Allocate1D<float>(totalPixels);
            }
            depthFloatBuffer.CopyFromCPU(depthFloats);

            RunAutoFocus();

            // Rolling-window filter
            if (rollingWindow == null)
                rollingWindow = new DepthRollingWindow(device, _targetWidth, _targetHeight, totalPixels);

            rollingWindow.AddFrame(depthFloatBuffer);

            using var filteredDepthBuffer = device.Allocate1D<float>(totalPixels);
            filterDepthRollingWindowKernel(totalPixels, rollingWindow.ToDevice(), filteredDepthBuffer.View);
            device.Synchronize();

            // (You can colorize using the filtered version, or unfiltered, depending on your preference)
            float[] filteredDepthFloats = new float[totalPixels];
            filteredDepthBuffer.CopyToCPU(filteredDepthFloats);

            // compute alpha/beta based on min/max
            float minVal = filteredDepthFloats.Min();
            float maxVal = filteredDepthFloats.Max();
            float range = maxVal - minVal;
            if (range < 1e-6f) range = 1e-6f;
            float alpha = 255.0f / range;
            float beta = -minVal * alpha;

            // colorize output: depth => BGRA + side-by-side original
            depthFloatsToBGRAImageKernel(
                outWidth * outHeight,
                depthFloatBuffer.View,
                inputImageGPU,
                reusableOutImage.toDevice(device),
                _targetWidth,
                _targetHeight,
                alpha,
                beta,
                RGBSwapBGR ? 1 : 0);
            device.Synchronize();

            // TAA
            int totalColorPixels = outWidth * outHeight;
            temporalAAKernel(
                totalColorPixels,
                reusableOutImage.toDevice(device),
                historyFrame.toDevice(device),
                historyFrame.toDevice(device),
                taa,
                frameCount);
            device.Synchronize();
            frameCount++;

            // Return final color
            return historyFrame;
        }

        public void Dispose()
        {
            _session.Dispose();
            inputFloatBuffer?.Dispose();
            depthFloatBuffer?.Dispose();
            rollingWindow?.Dispose();
            _faceDetector?.Dispose();

            _autoFocusAndStats?.Dispose();

            _httpClient?.Dispose();
        }
    }
}
