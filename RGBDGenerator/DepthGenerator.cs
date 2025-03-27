﻿using System;                // Base system namespace
                             // Required for fundamental types and exceptions
using System.Buffers;        // Array pooling and memory utilities
                             // Not used here, potential optimization opportunity
using System.ComponentModel; // INotifyPropertyChanged interfaces
                             // Unused in this GPU-focused class
using System.Linq;           // LINQ extension methods
                             // Used for tensor dimension validation
using System.Runtime.InteropServices; // Interop and memory operations
                                      // Critical for unsafe buffer copying
using GPU;                   // Custom GPU abstraction layer
                             // Central to ILGPU integration
using ILGPU;                 // ILGPU core namespace
                             // Foundation for GPU acceleration
using ILGPU.Runtime;         // GPU runtime components
using ILGPU.Runtime.CPU;

// Manages device memory and execution
using ILGPU.Runtime.Cuda;    // NVIDIA CUDA specific components
                             // Enables CUDA-specific optimizations
using Microsoft.ML.OnnxRuntime; // ONNX runtime integration
                                // Core of model inference capabilities
using Microsoft.ML.OnnxRuntime.Tensors; // Tensor data structures
                                        // Bridge between ONNX and .NET types
using OpenCvSharp;           // OpenCV wrapper (unused here)
                             // Potential image I/O expansion point
                             // Could enable remote inference scenarios

namespace LKG_NVIDIA_RAYS.Utils
{
    public sealed class DepthGenerator : IDisposable // Single-responsibility depth processor
                                                     // Implements dispose pattern for GPU resources
    {
        public Context context;
        public Accelerator device;
        public Action<Index1D, dImage, ArrayView<float>, int, int, float, int> imageToRGBFloatsKernel;
        public Action<Index1D, ArrayView<float>, dImage, dImage, int, int, float, float, int> depthFloatsToBGRAImageKernel;

        private readonly InferenceSession _session; // ONNX inference engine
                                                    // Long-lived for multiple inference runs
        private readonly int _targetWidth; // Configured output resolution
                                           // Fixed for pipeline consistency
        private readonly int _targetHeight; // Maintains aspect ratio requirements
                                            // Matches model input specs

        // GPU memory buffers stay device-resident between frames
        private MemoryBuffer1D<float, Stride1D.Dense>? inputFloatBuffer; // RGB input buffer
                                                                         // Prevents frame-to-frame reallocations
        private MemoryBuffer1D<float, Stride1D.Dense>? depthFloatBuffer; // Depth output buffer
                                                                         // Maintains GPU memory residency

        // CPU-side buffers avoid managed heap allocations
        private float[]? inputFloatData; // Normalized RGB input for ONNX
                                         // Pinned during inference
        private DenseTensor<float>? inputTensor; // ONNX-compatible input container
                                                 // Reused to prevent garbage collection
        private float[]? depthFloats; // Pre-allocated depth result buffer
                                      // Enables safe memory pinning

        private GPUImage? reusableOutImage; // Output image memory pool
                                            // Reduces GPU memory fragmentation

        private float border;

        public DepthGenerator(int size, string modelPath)
        {
            // Shape the size so that it becomes a multiple of 14.
            // This rounds down to the nearest multiple of 14.
            int adjustedSize = (int)Math.Floor(size / 14.0) * 14;
            // Ensure a minimum size of 14.
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

            // Use the shaped size for both targetWidth and targetHeight.
            _targetWidth = adjustedSize;
            _targetHeight = adjustedSize;
            border = 0.0f;

            var sessionOptions = SessionOptions.MakeSessionOptionWithCudaProvider(0);
            sessionOptions.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_VERBOSE;  // Verbose logging to check node placements
            _session = new InferenceSession(modelPath, sessionOptions);

            // Preallocation strategy for consistent performance
            int floatCount = 3 * _targetHeight * _targetWidth; // Input tensor size
            inputFloatData = new float[floatCount];

            // Tensor reuse for inference efficiency (NCHW format expected by ONNX)
            inputTensor = new DenseTensor<float>(inputFloatData, new[] { 1, 3, _targetHeight, _targetWidth });

            depthFloats = new float[_targetHeight * _targetWidth];
        }


        public void Dispose() // Resource cleanup implementation
                              // Critical for GPU memory management
        {
            _session.Dispose(); // ONNX session termination
                                // Releases native resources
            inputFloatBuffer?.Dispose(); // GPU buffer cleanup
                                         // Prevent VRAM leaks
            depthFloatBuffer?.Dispose(); // Depth buffer release
                                         // Frees up GPU resources
        }

        public GPUImage ComputeDepth(GPUImage inputImage, bool RGBSwapBGR = false)
        {
            /// STAGE 1: GPU PREPROCESSING ///
            /// 
            int totalPixels = _targetWidth * _targetHeight; // Frame size constant
                                                            // Used for buffer sizing

            // Dynamic buffer management
            if (inputFloatBuffer == null || inputFloatBuffer.Length < totalPixels * 3)
            {
                inputFloatBuffer?.Dispose(); // Clean old buffer first
                                             // Prevents VRAM fragmentation
                inputFloatBuffer = device.Allocate1D<float>(totalPixels * 3); // RGB buffer allocation
                                                                                  // Matches input tensor needs
            }

            // GPU image conversion kernel
            imageToRGBFloatsKernel(
                totalPixels, // Total threads (one per pixel)
                             // Massive parallelization
                inputImage.toDevice(device), // Source image transfer
                                          // Potential async optimization point
                inputFloatBuffer.View, // Destination view
                                       // Channel-packed RGB float output
                _targetWidth, // Resize target
                              // Maintains aspect ratio
                _targetHeight,
                border, 
                RGBSwapBGR ? 1 : 0);
            device.Synchronize(); // CPU-GPU synchronization
                                      // Framerate limiter, necessary for data safety

            // Copy to CPU for ONNX processing
            inputFloatBuffer.CopyToCPU(inputFloatData); // Device-to-host transfer
                                                        // Synchronous operation

            /// STAGE 2: ONNX INFERENCE ///

            var container = new List<NamedOnnxValue>(); // Input collection
                                                        // Could be reused
            container.Add(NamedOnnxValue.CreateFromTensor<float>("pixel_values", inputTensor!));
            // Named tensor binding
            // Requires model-specific name

            using (var outputs = _session.Run(container)) // Inference execution
                                                          // Bottleneck for latency
            {
                var output = outputs.First(); // Extract first output
                                              // Model-specific output index
                var depthTensor = output.AsTensor<float>(); // Type conversion
                                                            // Could validate element type

                ReadOnlySpan<int> dims = depthTensor.Dimensions; // Output shape
                                                                 // NHWC format validation
                if (dims.Length != 3 || dims[1] != _targetHeight || dims[2] != _targetWidth)
                    throw new Exception("Dimension mismatch"); // Model validation
                                                               // Critical for buffer safety

                unsafe // Low-level memory operations
                       // Necessary for zero-copy extraction
                {
                    var denseTensor = depthTensor as DenseTensor<float>; // Type assertion
                                                                         // Requires contiguous buffer
                    if (denseTensor?.Length != depthFloats.Length)
                        throw new InvalidOperationException("Invalid tensor format");
                    // Memory alignment check
                    // Prevents buffer overflow

                    using var sourceHandle = denseTensor.Buffer.Pin(); // Pinning for direct access
                                                                       // Avoids GC relocation
                    fixed (float* pDest = depthFloats) // Pin managed array
                                                       // Enables direct memory access
                    {
                        Buffer.MemoryCopy( // Unsafe bulk copy
                            sourceHandle.Pointer, // Source tensor data
                                                  // Direct from ONNX buffer
                            pDest, // Destination array
                                   // Pre-allocated for consistency
                            depthFloats.Length * sizeof(float), // Max copy size
                                                                // Safety guardrail
                            denseTensor.Length * sizeof(float) // Actual data size
                                                               // Match validated earlier
                        );
                    }
                }
            }

            /// STAGE 3: GPU POSTPROCESSING ///

            // Output image memory recycling
            int outWidth = inputImage.width * 2;
            int outHeight = inputImage.height;
            if (reusableOutImage == null ||
                reusableOutImage.width != outWidth ||
                reusableOutImage.height != outHeight)
            {
                reusableOutImage?.Dispose();
                reusableOutImage = new GPUImage(outWidth, outHeight);
            }


            // Dynamic range normalization
            float minVal = depthFloats.Min(); // CPU-side min calculation
                                              // Bottleneck, potential GPU offload
            float maxVal = depthFloats.Max(); // Range determination
                                              // Affects contrast stretching
            float range = maxVal - minVal; // Basis for normalization
                                           // Sensitive to depth precision
            if (range < 1e-6f)
                range = 1e-6f; // Zero-division protection
                               // Numerical stability safeguard
            float alpha = 255.0f / range; // Scaling factor
                                          // Maps depth to 8-bit range
            float beta = -minVal * alpha; // Offset term
                                          // Centers dynamic range

            // Depth buffer management
            if (depthFloatBuffer == null || depthFloatBuffer.Length < totalPixels)
            {
                depthFloatBuffer?.Dispose(); // Clean previous allocation
                                             // Maintains VRAM discipline
                depthFloatBuffer = device.Allocate1D<float>(totalPixels); // Single-channel buffer
                                                                              // Optimizes memory layout
            }
            depthFloatBuffer.CopyFromCPU(depthFloats); // Host-to-device transfer
                                                       // Synchronous data mirroring

            depthFloatsToBGRAImageKernel(
                outWidth * outHeight,
                depthFloatBuffer.View,          // The normalized depth data
                inputImage.toDevice(device),     // The original color image
                reusableOutImage.toDevice(device),
                _targetWidth,                    // The inference depthWidth
                _targetHeight,                   // The inference depthHeight
                alpha,
                beta, RGBSwapBGR ? 1 : 0);
            device.Synchronize();

            return reusableOutImage; // Final processed frame
                                     // GPU resident for display efficiency
        }
    }
}
