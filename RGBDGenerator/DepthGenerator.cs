using System;
using System.Buffers;
using System.ComponentModel;
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

namespace LKG_NVIDIA_RAYS.Utils
{
    public sealed class DepthGenerator : IDisposable
    {
        public Context context;
        public Accelerator device;
        public Action<Index1D, dImage, ArrayView<float>, int, int, float, int> imageToRGBFloatsKernel;
        public Action<Index1D, ArrayView<float>, dImage, dImage, int, int, float, float, int> depthFloatsToBGRAImageKernel;
        // New filtering kernel that uses the rolling window.
        public Action<Index1D, dDepthRollingWindow, ArrayView<float>> filterDepthRollingWindowKernel;

        private readonly InferenceSession _session;
        private readonly int _targetWidth;
        private readonly int _targetHeight;

        private MemoryBuffer1D<float, Stride1D.Dense>? inputFloatBuffer;
        private MemoryBuffer1D<float, Stride1D.Dense>? depthFloatBuffer;

        private float[]? inputFloatData;
        private DenseTensor<float>? inputTensor;
        private float[]? depthFloats;

        private GPUImage? reusableOutImage;

        public DepthRollingWindow? rollingWindow;

        private float border;

        public DepthGenerator(int size, string modelPath)
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
            // Load the new filtering kernel.
            filterDepthRollingWindowKernel = device.LoadAutoGroupedStreamKernel<Index1D, dDepthRollingWindow, ArrayView<float>>(Kernels.FilterDepthRollingWindow);

            _targetWidth = adjustedSize;
            _targetHeight = adjustedSize;
            border = 0.0f;

            using var cudaProviderOptions = new OrtCUDAProviderOptions(); // Dispose this finally

            var providerOptionsDict = new Dictionary<string, string>();
            providerOptionsDict["cudnn_conv_use_max_workspace"] = "1";
            providerOptionsDict["cudnn_conv1d_pad_to_nc1d"] = "1";

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
        }

        public void Dispose()
        {
            _session.Dispose();
            inputFloatBuffer?.Dispose();
            depthFloatBuffer?.Dispose();
            rollingWindow?.Dispose();
        }

        public GPUImage ComputeDepth(GPUImage inputImage, bool RGBSwapBGR = false)
        {
            int totalPixels = _targetWidth * _targetHeight;

            // STAGE 1: GPU PREPROCESSING
            if (inputFloatBuffer == null || inputFloatBuffer.Length < totalPixels * 3)
            {
                inputFloatBuffer?.Dispose();
                inputFloatBuffer = device.Allocate1D<float>(totalPixels * 3);
            }

            imageToRGBFloatsKernel(
                totalPixels,
                inputImage.toDevice(device),
                inputFloatBuffer.View,
                _targetWidth,
                _targetHeight,
                border,
                RGBSwapBGR ? 1 : 0);
            device.Synchronize();

            inputFloatBuffer.CopyToCPU(inputFloatData);

            // STAGE 2: ONNX INFERENCE
            var container = new List<NamedOnnxValue>();
            container.Add(NamedOnnxValue.CreateFromTensor<float>("pixel_values", inputTensor!));

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
                    if (denseTensor?.Length != depthFloats.Length)
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
            depthFloatBuffer.CopyFromCPU(depthFloats);

            // --- Update the rolling window and filter the depth ---
            if (rollingWindow == null)
                rollingWindow = new DepthRollingWindow(device, _targetWidth, _targetHeight, totalPixels);
            // Add the current depth frame into the circular buffer.
            rollingWindow.AddFrame(depthFloatBuffer);

            // Allocate a temporary buffer for the filtered depth.
            var filteredDepthBuffer = device.Allocate1D<float>(totalPixels);

            // Invoke the filtering kernel using the rolling window.
            filterDepthRollingWindowKernel(totalPixels, rollingWindow.ToDevice(), filteredDepthBuffer.View);
            device.Synchronize();

            // Compute dynamic range normalization based on the filtered depth values
            float[] filteredDepthFloats = new float[totalPixels];
            filteredDepthBuffer.CopyToCPU(filteredDepthFloats);
            float minVal = filteredDepthFloats.Min();
            float maxVal = filteredDepthFloats.Max();
            float range = maxVal - minVal;
            if (range < 1e-6f)
                range = 1e-6f;
            float alpha = 255.0f / range;
            float beta = -minVal * alpha;

            // Now use the filtered depth values for image postprocessing.
            depthFloatsToBGRAImageKernel(
                outWidth * outHeight,
                filteredDepthBuffer.View,
                inputImage.toDevice(device),
                reusableOutImage.toDevice(device),
                _targetWidth,
                _targetHeight,
                alpha,
                beta,
                RGBSwapBGR ? 1 : 0);
            device.Synchronize();

            filteredDepthBuffer.Dispose();
            return reusableOutImage;
        }

    }
}
