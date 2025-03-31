using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
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
        public Context context;
        public Accelerator device;
        public Action<Index1D, dImage, ArrayView<float>, int, int, float, int> imageToRGBFloatsKernel;
        public Action<Index1D, ArrayView<float>, dImage, dImage, int, int, float, float, int> depthFloatsToBGRAImageKernel;
        public Action<Index1D, dDepthRollingWindow, ArrayView<float>> filterDepthRollingWindowKernel;
        public Action<Index1D, ArrayView<float>, int, int, ArrayView<int>> AnalyzeDepthKernel;
        public Action<Index1D, ArrayView<float>, float, float, ArrayView<float>> RemapDepthKernel;
        public Action<Index1D, ArrayView<int>, ArrayView<float>> FindFocusDepthKernel;

        private readonly InferenceSession _session;
        private int _targetWidth;
        private int _targetHeight;

        private MemoryBuffer1D<float, Stride1D.Dense>? inputFloatBuffer;
        private MemoryBuffer1D<float, Stride1D.Dense>? depthFloatBuffer;

        private float[]? inputFloatData;
        private DenseTensor<float>? inputTensor;
        private float[]? depthFloats;
        private GPUImage? reusableOutImage;
        public DepthRollingWindow? rollingWindow;

        public MemoryBuffer1D<int, Stride1D.Dense> DepthHistogram;
        public MemoryBuffer1D<float, Stride1D.Dense> FocusResult;
        public MemoryBuffer1D<float, Stride1D.Dense> DepthMinMax;
        public MemoryBuffer1D<float, Stride1D.Dense> FaceBoxesBuffer;

        // Auto-focus properties
        public bool AutoFocusEnabled { get; set; }
        public float AutoFocusStrength { get; set; }
        public bool AutoFocusUseFaces { get; set; }
        public float LastFocusDepth { get; set; }
        public float FocusSmoothing { get; set; }

        private float border;

        // Optional: Hold a reference to the face detector
        private readonly FaceDetector? _faceDetector;

        // If you want to access or store the last set of faces:
        public List<FaceBox>? LastDetectedFaces { get; private set; }

        public DepthGenerator(int size, string modelPath, string? faceModelPath = null)
        {
            int adjustedSize = (int)Math.Floor(size / 14.0) * 14;
            if (adjustedSize < 14)
                adjustedSize = 14;

            bool debug = false;

            context = Context.Create(builder => builder.CPU().Cuda()
                .EnableAlgorithms()
                .Math(MathMode.Fast32BitOnly)
                .Inlining(InliningMode.Aggressive)
                .AutoAssertions()
                .Optimize(OptimizationLevel.O1));
            device = context.GetPreferredDevice(preferCPU: debug).CreateAccelerator(context);

            imageToRGBFloatsKernel = device.LoadAutoGroupedStreamKernel<Index1D, dImage, ArrayView<float>, int, int, float, int>(Kernels.ImageToRGBFloats);
            depthFloatsToBGRAImageKernel = device.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, dImage, dImage, int, int, float, float, int>(Kernels.DepthFloatsToBGRAImageFull);
            filterDepthRollingWindowKernel = device.LoadAutoGroupedStreamKernel<Index1D, dDepthRollingWindow, ArrayView<float>>(Kernels.FilterDepthRollingWindow);

            _targetWidth = adjustedSize;
            _targetHeight = adjustedSize;
            border = 0.0f;

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
            sessionOptions.InterOpNumThreads = 16;
            sessionOptions.IntraOpNumThreads = 16;

            _session = new InferenceSession(modelPath, sessionOptions);

            int floatCount = 3 * _targetHeight * _targetWidth;
            inputFloatData = new float[floatCount];
            inputTensor = new DenseTensor<float>(inputFloatData, new[] { 1, 3, _targetHeight, _targetWidth });
            depthFloats = new float[_targetHeight * _targetWidth];

            // If a face model path was provided, initialize the face detector
            if (!string.IsNullOrEmpty(faceModelPath))
            {
                _faceDetector = new FaceDetector(faceModelPath, device);
            }
        }

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

        public void InitializeAutoFocus()
        {
            // You'd add these fields to the DepthGenerator class
            AnalyzeDepthKernel = device.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, int, int, ArrayView<int>>(Kernels.AnalyzeDepthForAutoFocus);
            RemapDepthKernel = device.LoadAutoGroupedStreamKernel<Index1D, ArrayView<float>, float, float, ArrayView<float>>(Kernels.RemapDepthForAutoFocus);
            FindFocusDepthKernel = device.LoadAutoGroupedStreamKernel<Index1D, ArrayView<int>, ArrayView<float>>(Kernels.FindOptimalFocusDepth);

            // Create buffers for histogram and focus result
            DepthHistogram = device.Allocate1D<int>(256);
            FocusResult = device.Allocate1D<float>(2);  // [focusDepth, confidence]
            DepthMinMax = device.Allocate1D<float>(2);  // [min, max]

            // Add a face boxes buffer for when face detection is used
            FaceBoxesBuffer = device.Allocate1D<float>(50 * 5);  // Up to 50 faces, 5 values per face

            // Add properties to control auto-focus
            AutoFocusEnabled = false;
            AutoFocusStrength = 0.7f;
            AutoFocusUseFaces = true;
            LastFocusDepth = 0.5f;  // Initialize to mid-range
            FocusSmoothing = 0.8f;  // How much to smooth focus changes between frames
        }

        // Add this method to apply auto-focus in the DepthGenerator.ComputeDepth method
        public void ApplyAutoFocus(float[] depthFloats)
        {
            if (!AutoFocusEnabled)
                return;

            int totalPixels = depthFloats.Length;

            // Copy depth data to GPU if not already there
            depthFloatBuffer.CopyFromCPU(depthFloats);

            // Calculate min/max depth for normalization
            float minDepth = float.MaxValue;
            float maxDepth = float.MinValue;
            foreach (float d in depthFloats)
            {
                if (d > 0) // Skip invalid depths
                {
                    minDepth = Math.Min(minDepth, d);
                    maxDepth = Math.Max(maxDepth, d);
                }
            }

            // Update min/max on GPU
            float[] minMaxArray = new float[] { minDepth, maxDepth };
            DepthMinMax.CopyFromCPU(minMaxArray);

            // Clear histogram
            DepthHistogram.MemSetToZero();

            // Analyze depth to build histogram
            AnalyzeDepthKernel(
                totalPixels,
                depthFloatBuffer.View,
                _targetWidth,
                _targetHeight,
                DepthHistogram.View);
            device.Synchronize();

            // Find optimal focus depth from histogram
            FindFocusDepthKernel(1, DepthHistogram.View, FocusResult.View);
            device.Synchronize();

            // Get focus results
            float[] focusResults = new float[2];
            FocusResult.CopyToCPU(focusResults);

            float focusDepth = focusResults[0];
            float confidence = focusResults[1];

            // Apply temporal smoothing to focus depth changes
            LastFocusDepth = LastFocusDepth * FocusSmoothing +
                                     focusDepth * (1 - FocusSmoothing);

            // Remap depth values based on target focus
            RemapDepthKernel(
                totalPixels,
                depthFloatBuffer.View,
                LastFocusDepth,
                AutoFocusStrength,
                DepthMinMax.View);
            device.Synchronize();

            // Copy results back to CPU
            depthFloatBuffer.CopyToCPU(depthFloats);
        }

        public GPUImage ComputeDepth(GPUImage inputImage, bool RGBSwapBGR = false, bool detectFaces = false)
        {
            dImage inputImageGPU = inputImage.toDevice(device);

            // Optionally run face detection
            if (detectFaces && _faceDetector != null)
            {
                List<FaceBox> faces = _faceDetector.DetectFaces(inputImageGPU, threshold: 0.7f);
            }

            int totalPixels = _targetWidth * _targetHeight;

            // STAGE 1: GPU PREPROCESSING
            if (inputFloatBuffer == null || inputFloatBuffer.Length < totalPixels * 3)
            {
                inputFloatBuffer?.Dispose();
                inputFloatBuffer = device.Allocate1D<float>(totalPixels * 3);
            }

            imageToRGBFloatsKernel(
                totalPixels,
                inputImageGPU,
                inputFloatBuffer.View,
                _targetWidth,
                _targetHeight,
                border,
                RGBSwapBGR ? 1 : 0);
            device.Synchronize();

            inputFloatBuffer.CopyToCPU(inputFloatData);

            // STAGE 2: ONNX INFERENCE
            var container = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor<float>("pixel_values", inputTensor!)
            };

            using (var outputs = _session.Run(container))
            {
                var output = outputs.First();
                var depthTensor = output.AsTensor<float>();

                ReadOnlySpan<int> dims = depthTensor.Dimensions;
                if (dims.Length != 3 || dims[1] != _targetHeight || dims[2] != _targetWidth)
                    throw new Exception("Dimension mismatch");

                unsafe
                {
                    var denseTensor = depthTensor as DenseTensor<float>;
                    if (denseTensor?.Length != depthFloats!.Length)
                        throw new InvalidOperationException("Invalid tensor format");

                    using var sourceHandle = denseTensor.Buffer.Pin();
                    fixed (float* pDest = depthFloats)
                    {
                        Buffer.MemoryCopy(
                            sourceHandle.Pointer,
                            pDest,
                            depthFloats.Length * sizeof(float),
                            denseTensor.Length * sizeof(float));
                    }
                }
            }

            // STAGE 3: GPU POSTPROCESSING
            int outWidth = inputImage.width * 2;
            int outHeight = inputImage.height;
            if (reusableOutImage == null || reusableOutImage.width != outWidth || reusableOutImage.height != outHeight)
            {
                reusableOutImage?.Dispose();
                reusableOutImage = new GPUImage(outWidth, outHeight);
            }

            if (depthFloatBuffer == null || depthFloatBuffer.Length < totalPixels)
            {
                depthFloatBuffer?.Dispose();
                depthFloatBuffer = device.Allocate1D<float>(totalPixels);
            }
            depthFloatBuffer.CopyFromCPU(depthFloats!);

            if (rollingWindow == null)
                rollingWindow = new DepthRollingWindow(device, _targetWidth, _targetHeight, totalPixels);

            rollingWindow.AddFrame(depthFloatBuffer);

            var filteredDepthBuffer = device.Allocate1D<float>(totalPixels);
            filterDepthRollingWindowKernel(totalPixels, rollingWindow.ToDevice(), filteredDepthBuffer.View);
            device.Synchronize();

            float[] filteredDepthFloats = new float[totalPixels];
            filteredDepthBuffer.CopyToCPU(filteredDepthFloats);
            filteredDepthBuffer.Dispose();

            float minVal = filteredDepthFloats.Min();
            float maxVal = filteredDepthFloats.Max();
            float range = maxVal - minVal;
            if (range < 1e-6f)
                range = 1e-6f;

            float alpha = 255.0f / range;
            float beta = -minVal * alpha;

            depthFloatsToBGRAImageKernel(
                outWidth * outHeight,
                depthFloatBuffer.View,
                inputImage.toDevice(device),
                reusableOutImage.toDevice(device),
                _targetWidth,
                _targetHeight,
                alpha,
                beta,
                RGBSwapBGR ? 1 : 0);
            device.Synchronize();

            return reusableOutImage;
        }

        public void Dispose()
        {
            _session.Dispose();
            inputFloatBuffer?.Dispose();
            depthFloatBuffer?.Dispose();
            rollingWindow?.Dispose();
            _faceDetector?.Dispose();
        }
    }

}
