using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace NullEngine.Utils
{
    public class FpsCounter
    {
        private readonly Queue<float> frameTimes = new Queue<float>();
        private readonly Queue<float> fiveSecondFrameTimes = new Queue<float>();
        private float lastFrameTimeMs;
        private float minFps = float.MaxValue;
        private float totalTime1Sec = 0;
        private float totalTime5Sec = 0;

        public void Update(float frameTime)
        {
            lastFrameTimeMs = frameTime * 1000.0f; // Convert to milliseconds
            float fps = 1.0f / frameTime;

            // Update minimum FPS
            if (fps < minFps)
            {
                minFps = fps;
            }

            // Update 1-second FPS
            totalTime1Sec += frameTime;
            frameTimes.Enqueue(frameTime);
            while (totalTime1Sec > 1.0f)
            {
                totalTime1Sec -= frameTimes.Dequeue();
            }

            // Update 5-second FPS
            totalTime5Sec += frameTime;
            fiveSecondFrameTimes.Enqueue(frameTime);
            while (totalTime5Sec > 5.0f)
            {
                totalTime5Sec -= fiveSecondFrameTimes.Dequeue();
            }
        }

        public float GetLastFrameTimeMs()
        {
            return lastFrameTimeMs;
        }

        public float GetAverageFps1Sec()
        {
            return frameTimes.Count > 0 ? frameTimes.Count / totalTime1Sec : 0;
        }

        public float GetAverageFps5Sec()
        {
            return fiveSecondFrameTimes.Count > 0 ? fiveSecondFrameTimes.Count / totalTime5Sec : 0;
        }

        public float GetMinFps()
        {
            return minFps;
        }
    }

}
