using System;
using System.Collections.Generic;
using System.Linq;
using ILGPU;
using ILGPU.Runtime;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using GPU;  // For GPUImage, dImage, etc.

namespace RGBDGenerator
{
    public class FaceDetector : IDisposable
    {
        private readonly InferenceSession _faceSession;
        private readonly Accelerator _device;

        private MemoryBuffer1D<float, Stride1D.Dense> _faceInputBuffer;  // GPU float buffer
        private float[]? _cpuFloatData;                                   // CPU float buffer for ONNX

        // UltraFace RFB-320 default
        private const int TargetWidth = 320;
        private const int TargetHeight = 240;

        private const float MeanVal = 127f;
        private const float NormVal = 1f / 128f;

        // ILGPU kernel delegate
        private Action<Index1D, dImage, ArrayView1D<float, Stride1D.Dense>, int, int, float, float>? _faceToCHWKernel;

        public FaceDetector(string modelPath, Accelerator device)
        {
            _device = device;

            // 2) Load kernel
            _faceToCHWKernel = _device.LoadAutoGroupedStreamKernel<
                Index1D, dImage, ArrayView1D<float, Stride1D.Dense>, int, int, float, float
            >(Kernels.FaceToCHWFloats);

            // Allocate a single GPU buffer for 3 * 320 * 240 floats
            int floatCount = 3 * TargetWidth * TargetHeight;
            _faceInputBuffer = _device.Allocate1D<float>(floatCount);
            _cpuFloatData = new float[floatCount];


            using SessionOptions sessionOptions = SessionOptions.MakeSessionOptionWithCudaProvider(0);
            sessionOptions.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_VERBOSE;

            _faceSession = new InferenceSession(modelPath, sessionOptions);
        }

        /// <summary>
        /// Main inference entry point.  Resizes & normalizes input GPUImage to [3,240,320] on GPU,
        /// copies float buffer to CPU, runs ONNX, decodes boxes, and returns them.
        /// </summary>
        public List<FaceBox> DetectFaces(dImage inputImage, float threshold = 0.7f)
        {
            int totalPixels = TargetWidth * TargetHeight;
            int floatCount = 3 * totalPixels;

            _device.Synchronize();

            try
            {
                // Run the kernel
                _faceToCHWKernel(
                    totalPixels,
                    inputImage,
                    _faceInputBuffer,
                    TargetWidth,
                    TargetHeight,
                    MeanVal,
                    NormVal);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.ToString());
            }

            _device.Synchronize();

            // 2) Copy that float data from GPU to CPU
            _faceInputBuffer.CopyToCPU(_cpuFloatData);

            // 3) Run inference with OnnxRuntime
            var container = new List<NamedOnnxValue>(1);
            // UltraFace typically wants shape [1,3,240,320]
            var inputTensor = new DenseTensor<float>(_cpuFloatData, new int[] { 1, 3, TargetHeight, TargetWidth });

            // The actual input name depends on your model.  E.g., "input"
            string inputName = _faceSession.InputMetadata.Keys.First();
            container.Add(NamedOnnxValue.CreateFromTensor(inputName, inputTensor));

            float[] confidences;
            float[] boxes;
            using (var results = _faceSession.Run(container))
            {
                // By convention, the first output is confidences, second is boxes
                // Adapt to your actual model's node names or output order
                confidences = results.ElementAt(0).AsEnumerable<float>().ToArray();
                boxes = results.ElementAt(1).AsEnumerable<float>().ToArray();
            }

            // 4) Decode bounding boxes on CPU (numeric only)
            List<FaceBox> faceBoxes = DecodeUltraFaceOutputs(
                inputImage.width,  // original image dimensions
                inputImage.height,
                confidences,
                boxes,
                threshold
            );

            // Optionally do "square" bounding boxes
            for (int i = 0; i < faceBoxes.Count; i++)
            {
                int[] scaled = ScaleBoundingBox(new int[]
                {
                    faceBoxes[i].X1,
                    faceBoxes[i].Y1,
                    faceBoxes[i].X2,
                    faceBoxes[i].Y2
                });
                faceBoxes[i] = new FaceBox
                {
                    X1 = scaled[0],
                    Y1 = scaled[1],
                    X2 = scaled[2],
                    Y2 = scaled[3],
                    Probability = faceBoxes[i].Probability
                };
            }

            return faceBoxes;
        }

        /// <summary>
        /// CPU logic to decode UltraFace bounding boxes from the raw outputs.
        /// Adapt to match your 'box_utils' or 'predict(...)' exactly.
        /// </summary>
        private static List<FaceBox> DecodeUltraFaceOutputs(
            int imgWidth,
            int imgHeight,
            float[] confidences,
            float[] boxes,
            float threshold)
        {
            int numAnchors = confidences.Length / 2;
            var results = new List<FaceBox>();

            for (int i = 0; i < numAnchors; i++)
            {
                float scoreFace = confidences[i * 2 + 1];
                if (scoreFace < threshold)
                    continue;

                // boxes: x1, y1, x2, y2 in [0..1] or depends on the model
                // If your model is center/width/height, adapt accordingly.
                float x1 = boxes[i * 4 + 0] * imgWidth;
                float y1 = boxes[i * 4 + 1] * imgHeight;
                float x2 = boxes[i * 4 + 2] * imgWidth;
                float y2 = boxes[i * 4 + 3] * imgHeight;

                int ix1 = (int)Math.Round(x1);
                int iy1 = (int)Math.Round(y1);
                int ix2 = (int)Math.Round(x2);
                int iy2 = (int)Math.Round(y2);

                if (ix1 < 0) ix1 = 0; if (iy1 < 0) iy1 = 0;
                if (ix2 > imgWidth) ix2 = imgWidth;
                if (iy2 > imgHeight) iy2 = imgHeight;

                if (ix2 - ix1 <= 0 || iy2 - iy1 <= 0)
                    continue;

                results.Add(new FaceBox
                {
                    X1 = ix1,
                    Y1 = iy1,
                    X2 = ix2,
                    Y2 = iy2,
                    Probability = scoreFace
                });
            }
            return results;
        }

        /// <summary>
        /// Optionally make bounding boxes square by expanding the shorter dimension.
        /// Replicates the 'scale()' in Python.
        /// </summary>
        private static int[] ScaleBoundingBox(int[] box)
        {
            int w = box[2] - box[0];
            int h = box[3] - box[1];
            int maxSide = Math.Max(w, h);
            int dx = (maxSide - w) / 2;
            int dy = (maxSide - h) / 2;
            return new int[]
            {
                box[0] - dx,
                box[1] - dy,
                box[2] + dx,
                box[3] + dy
            };
        }

        public void Dispose()
        {
            _faceSession.Dispose();
            _faceInputBuffer?.Dispose();
            _device.Dispose();
        }
    }

    public struct FaceBox
    {
        public int X1;
        public int Y1;
        public int X2;
        public int Y2;
        public float Probability;
    }
}
