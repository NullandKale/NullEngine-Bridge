using ILGPU.Runtime;
using ILGPU;
using ILGPU.Algorithms;

namespace GPU
{
    public static partial class Kernels
    {
        public static void ImageToRGB(Index1D index, ArrayView1D<byte, Stride1D.Dense> output, dImage input)
        {
            int x = index.X % input.width;
            int y = index.X / input.width;

            RGBA32 color = input.GetColorAt(x, y);

            output[index * 3 + 0] = color.r;
            output[index * 3 + 1] = color.g;
            output[index * 3 + 2] = color.b;
        }

        public static void RGBToImage(Index1D index, dImage output, ArrayView1D<byte, Stride1D.Dense> input)
        {
            int x = index.X % output.width;
            int y = index.X / output.width;

            RGBA32 color = new RGBA32(0, 0, 0, 255);

            color.r = input[index * 3 + 0];
            color.g = input[index * 3 + 1];
            color.b = input[index * 3 + 2];

            output.SetColorAt(x, y, color);
        }

        private static float GenerateRandomValue(int sequenceX, int sequenceY, int tick)
        {
            // Re-seed the random number generator with the sequence index to ensure repeatable results for each index
            int seed = sequenceX * 32 + sequenceY;

            // Shuffle the random number generator's internal state to add additional randomness
            for (int i = 0; i < tick % 10; i++)
            {
                seed = (seed * 1103515245 + 12345) % 2147483647;
            }

            // Use the re-seeded and shuffled random number generator to generate a random value for the given sequence index
            double randomValue = ((sequenceX + 1) * (sequenceY + 1) * seed) % 1000000.0 / 1000000.0;

            return (float)randomValue;
        }

        private static Vec2 GetJitteredUV(int tick, float u, float v, float uMin, float vMin)
        {
            // Define the dimensions of the sequence
            const int sequenceWidth = 32;
            const int sequenceHeight = 32;

            // Calculate the current index within the sequence
            int sequenceX = (int)(u * sequenceWidth);
            int sequenceY = (int)(v * sequenceHeight);

            // Generate a random value for the given index
            float randomValue = GenerateRandomValue(sequenceX, sequenceY, tick);

            // Calculate the jittered u and v values
            float jitteredU = u + uMin * randomValue;
            float jitteredV = v + vMin * randomValue;

            // Return the jittered u and v values as a Vec2
            return new Vec2(jitteredU, jitteredV);
        }

        /// <summary>
        /// Kernel to scale an input image (in RGBA) to a target resolution and convert to an array of RGB floats 
        /// in NCHW format (i.e. shape [1, 3, outHeight, outWidth]). The red, green, and blue channels are stored 
        /// contiguously per channel.
        /// </summary>
        public static void ImageToRGBFloats(
            Index1D index,
            dImage input,
            ArrayView<float> output,
            int outWidth,
            int outHeight,
            float border,
            int rgbSwapBgr)
        {
            int totalPixels = outWidth * outHeight;
            if (index >= totalPixels)
                return;

            // Compute output pixel coordinates (in HWC order)
            int x = index % outWidth;
            int y = index / outWidth;

            // Compute normalized coordinates (center of pixel, in [0,1])
            float u = (x + 0.5f) / outWidth;
            float v = (y + 0.5f) / outHeight;

            // Adjust coordinates to zoom toward the center while leaving a border.
            // When border is 0, use the full image.
            // When border is 1, the effective image shrinks to a point.
            float effectiveRegion = 1.0f - border;
            float uAdjusted = border * 0.5f + u * effectiveRegion;
            float vAdjusted = border * 0.5f + v * effectiveRegion;

            // Map to input image coordinates
            float inX = uAdjusted * input.width;
            float inY = vAdjusted * input.height;

            // Nearest-neighbor sampling
            int srcX = (int)inX;
            int srcY = (int)inY;
            if (srcX >= input.width)
                srcX = input.width - 1;
            if (srcY >= input.height)
                srcY = input.height - 1;

            // Retrieve the color and convert to Vec3.
            // Assuming that toVec3() converts the color components to floats.
            Vec3 color = input.GetColorAt(srcX, srcY).toVec3();

            // If the color components are in [0,255], divide by 255.0f.
            // Here, we assume they are already normalized to [0,1]:
            if (rgbSwapBgr == 0)
            {
                float r = color.z;
                float g = color.y;
                float b = color.x;

                // Write values in NCHW order:
                // - Red channel at offset 0,
                // - Green channel at offset totalPixels,
                // - Blue channel at offset 2 * totalPixels.
                int pixelIndex = y * outWidth + x;
                output[pixelIndex] = r;
                output[pixelIndex + totalPixels] = g;
                output[pixelIndex + 2 * totalPixels] = b;
            }
            else
            {
                float r = color.x;
                float g = color.y;
                float b = color.z;

                // Write values in NCHW order:
                // - Red channel at offset 0,
                // - Green channel at offset totalPixels,
                // - Blue channel at offset 2 * totalPixels.
                int pixelIndex = y * outWidth + x;
                output[pixelIndex] = r;
                output[pixelIndex + totalPixels] = g;
                output[pixelIndex + 2 * totalPixels] = b;
            }

        }

        /// <summary>
        /// Writes an output image of size (colorWidth * 2) x (colorHeight).
        /// The left half is the color from <paramref name="colorImage"/> (full resolution),
        /// and the right half is the depth data (converted to grayscale) from <paramref name="depthInput"/>.
        /// The depth input has resolution depthWidth x depthHeight (the ONNX inference size).
        /// </summary>
        public static void DepthFloatsToBGRAImageFull(
            Index1D index,
            ArrayView<float> depthInput,
            dImage colorImage,  // The original color image at full resolution
            dImage output,      // The final output image: width = colorImage.width * 2, height = colorImage.height
            int depthWidth,
            int depthHeight,
            float alpha,
            float beta,
            int rgbSwapBGR)
        {
            int totalPixels = output.width * output.height;
            if (index >= totalPixels)
                return;

            // Convert linear index to (x, y) in the final output image.
            int x = index % output.width;
            int y = index / output.width;

            // The left half is color, the right half is depth.
            int colorWidth = colorImage.width;   // The original input image width
            int colorHeight = colorImage.height; // The original input image height

            if (x < colorWidth)
            {
                // ----- LEFT HALF: copy the color directly -----
                // Just read from the color image at (x,y).
                // Make sure we clamp if y >= colorHeight, etc. (in practice it should match exactly).
                if (x < colorWidth && y < colorHeight)
                {
                    RGBA32 c = colorImage.GetColorAt(x, y);
                    if (rgbSwapBGR == 0)
                    {
                        output.SetColorAt(x, y, c);

                    }
                    else
                    {
                        output.SetColorAt(x, y, new RGBA32(c.toVec3()));
                    }
                }
            }
            else
            {
                // ----- RIGHT HALF: convert depth to grayscale -----
                // We'll map x from [colorWidth .. (colorWidth*2 - 1)] to [0..1] in u,
                // and y from [0..(colorHeight - 1)] to [0..1] in v,
                // then sample the depth data (depthWidth x depthHeight).
                int x2 = x - colorWidth; // zero-based coordinate in the right half
                if (y < colorHeight)
                {
                    float u = x2 / (float)colorWidth;  // in [0..1]
                    float v = y / (float)colorHeight;  // in [0..1]

                    // Convert (u,v) to indices in the depth array
                    int dX = (int)(u * depthWidth);
                    int dY = (int)(v * depthHeight);
                    if (dX >= depthWidth) dX = depthWidth - 1;
                    if (dY >= depthHeight) dY = depthHeight - 1;

                    int depthIndex = dY * depthWidth + dX;
                    float depthVal = depthInput[depthIndex];

                    // Scale and clamp to [0..255].
                    float scaled = depthVal * alpha + beta;
                    if (scaled < 0f) scaled = 0f;
                    else if (scaled > 255f) scaled = 255f;

                    byte gray = (byte)scaled;
                    RGBA32 depthColor = new RGBA32(gray, gray, gray, 255);
                    output.SetColorAt(x, y, depthColor);
                }
            }
        }

        /// <summary>
        /// Kernel that resizes and normalizes a GPUImage into a float array (CHW layout),
        /// suitable for UltraFace-320 or other face detectors that expect [1,3,H,W].
        /// 
        /// The output shape is effectively: [3, outHeight, outWidth].
        /// So, 'output' must have length = 3 * outWidth * outHeight.
        /// The index we get is in [0..(outWidth*outHeight-1)].
        /// 
        /// MeanVal and NormVal let you do:   (pixel - meanVal) * normVal
        /// e.g., MeanVal=127, NormVal=1/128 =>  (color - 127)/128
        /// </summary>
        public static void FaceToCHWFloats(
            Index1D idx,
            dImage srcImage,
            ArrayView1D<float, Stride1D.Dense> output,  // length = 3 * outW * outH
            int outWidth,
            int outHeight,
            float meanVal,
            float normVal)
        {
            int totalPixels = outWidth * outHeight;
            if (idx >= totalPixels)
                return;

            int x = idx % outWidth;
            int y = idx / outWidth;

            // Convert to normalized [0..1] in the output space
            float u = (x + 0.5f) / outWidth;
            float v = (y + 0.5f) / outHeight;

            // Map to source image coordinates
            float inX = u * srcImage.width;
            float inY = v * srcImage.height;

            // Nearest neighbor
            int sx = XMath.Min((int)inX, srcImage.width - 1);
            int sy = XMath.Min((int)inY, srcImage.height - 1);

            // RGBA32 => .r,.g,.b in [0..255]
            RGBA32 color = srcImage.GetColorAt(sx, sy);
            float r = color.r;
            float g = color.g;
            float b = color.b;

            // Apply (pixel - meanVal)*normVal
            r = (r - meanVal) * normVal;
            g = (g - meanVal) * normVal;
            b = (b - meanVal) * normVal;

            // UltraFace typically uses input layout: [1, 3, H, W] in (R, G, B) channel order.
            // So store CHW: 
            //   output[ 0*totalPixels + idx ] = R
            //   output[ 1*totalPixels + idx ] = G
            //   output[ 2*totalPixels + idx ] = B
            output[idx] = r; // Channel 0
            output[idx + totalPixels] = g; // Channel 1
            output[idx + 2 * totalPixels] = b; // Channel 2
        }
    }
}
