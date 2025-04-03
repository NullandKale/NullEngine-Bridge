using System;

namespace NullEngine.Video
{
    /// <summary>
    /// Common interface for asynchronous frame readers (e.g., for cameras or video files).
    /// </summary>
    public interface IFrameReader : IDisposable
    {
        /// <summary>
        /// The width of the frame in pixels.
        /// </summary>
        int Width { get; }

        /// <summary>
        /// The height of the frame in pixels.
        /// </summary>
        int Height { get; }

        /// <summary>
        /// The frames per second.
        /// </summary>
        double Fps { get; }

        /// <summary>
        /// Indicates if the reader has looped back to the beginning.
        /// </summary>
        bool HasLooped { get; }

        /// <summary>
        /// In single-frame mode, advances to the next frame. In automatic mode, this may be a no-op.
        /// </summary>
        void PopFrame();

        /// <summary>
        /// Returns a pointer to the current frame’s data.
        /// </summary>
        IntPtr GetCurrentFramePtr();

        void Stop();
    }
}
