// StreamClient.cs – resilient to dropped / malformed MJPEG parts
using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace Holostream.Network
{
    public sealed class StreamClient : IDisposable
    {
        private readonly HttpClient httpClient = new HttpClient();
        private readonly string baseUri;
        private Stream pullStream;
        private string boundary;
        private bool pulling;
        private int streamWidth, streamHeight, streamQuality;
        private readonly ArrayPool<byte> pool = ArrayPool<byte>.Shared;
        private byte[] jpegBuf;
        private MjpegPushContent pushContent;
        private Task pushTask;
        private readonly MemoryStream pushMs = new MemoryStream(2 << 20);
        private readonly EncoderParameters jpgParams = new EncoderParameters(1);
        private readonly ImageCodecInfo jpgEnc =
            ImageCodecInfo.GetImageDecoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

        public StreamClient(string serverUri)
        {
            baseUri = serverUri.TrimEnd('/');
            jpgParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 95L);
        }

        public void Start(int width, int height, int quality, bool startCamera = false)
        {
            if (pulling) return;
            streamWidth = width; streamHeight = height; streamQuality = quality;
            jpgParams.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            if (startCamera)
                httpClient.PostAsync($"{baseUri}/start_camera", new StringContent("")).GetAwaiter().GetResult().EnsureSuccessStatusCode();
            var r = httpClient.GetAsync($"{baseUri}/mjpeg?width={width}&height={height}&quality={quality}",
                                         HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            r.EnsureSuccessStatusCode();
            boundary = "--" + r.Content.Headers.ContentType.Parameters.First(p => p.Name.Equals("boundary", StringComparison.OrdinalIgnoreCase)).Value.Trim('"');
            pullStream = r.Content.ReadAsStream();
            pulling = true;
        }

        public Bitmap Update()
        {
            if (!pulling) return null;
            try
            {
                if (!SyncToBoundary()) return null;                    // lost stream
                int len = ParseHeaders();                              // may throw
                if (len <= 0) return null;
                EnsureBuffer(len);
                if (!ReadBody(len)) return null;                       // incomplete → skip
                using var ms = new MemoryStream(jpegBuf, 0, len, false, true);
                return new Bitmap(ms);                                 // may throw (corrupt)
            }
            catch
            {
                return null;                                           // skip bad frame
            }
        }

        private bool SyncToBoundary()
        {
            var target = Encoding.ASCII.GetBytes("\r\n" + boundary + "\r\n");
            int idx = 0;
            while (true)
            {
                int b = pullStream.ReadByte();
                if (b < 0) return false;
                if (b == target[idx]) { if (++idx == target.Length) return true; }
                else idx = b == target[0] ? 1 : 0;
            }
        }

        private int ParseHeaders()
        {
            string line; int len = 0;
            while (!string.IsNullOrEmpty(line = ReadLine()))
                if (line.StartsWith("Content-Length", StringComparison.OrdinalIgnoreCase))
                    len = int.Parse(line[(line.IndexOf(':') + 1)..]);
            return len;
        }

        private void EnsureBuffer(int len)
        {
            if (jpegBuf == null || jpegBuf.Length < len)
            {
                if (jpegBuf != null) pool.Return(jpegBuf);
                jpegBuf = pool.Rent(len);
            }
        }

        private bool ReadBody(int len)
        {
            int off = 0;
            while (off < len)
            {
                int n = pullStream.Read(jpegBuf, off, len - off);
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }

        private string ReadLine()
        {
            var sb = new StringBuilder();
            int prev = -1;
            while (true)
            {
                int cur = pullStream.ReadByte();
                if (cur < 0) return null;
                if (prev == '\r' && cur == '\n') { sb.Length--; return sb.ToString(); }
                sb.Append((char)cur); prev = cur;
            }
        }

        public void StartTakeover()
        {
            httpClient.PostAsync($"{baseUri}/start_takeover", new StringContent("")).GetAwaiter().GetResult().EnsureSuccessStatusCode();
            pushContent = new MjpegPushContent("frame");
            var req = new HttpRequestMessage(HttpMethod.Post, $"{baseUri}/takeover_mjpeg") { Content = pushContent };
            req.Headers.TransferEncodingChunked = true;
            pushTask = httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead);
        }

        public void SendTakeoverFrame(nint ptr, int w, int h)
        {
            if (pushContent == null) throw new InvalidOperationException("takeover not started");
            using var bmp = new Bitmap(w, h, w * 3, PixelFormat.Format24bppRgb, (IntPtr)ptr);
            pushMs.SetLength(0); bmp.Save(pushMs, jpgEnc, jpgParams);
            pushContent.Push(pushMs.ToArray());
        }

        public void Stop()
        {
            if (pulling)
            {
                httpClient.PostAsync($"{baseUri}/stop_camera", new StringContent("")).GetAwaiter().GetResult();
                pullStream.Dispose(); pulling = false;
            }
            pushContent?.Complete(); pushTask?.Wait(); pushContent = null;
        }

        public void Dispose()
        {
            Stop(); httpClient.Dispose();
            if (jpegBuf != null) pool.Return(jpegBuf); pushMs.Dispose();
        }

        private sealed class MjpegPushContent : HttpContent
        {
            private readonly BlockingCollection<byte[]> q = new(new ConcurrentQueue<byte[]>());
            private readonly byte[] hdrPre, tail;
            public MjpegPushContent(string b)
            {
                Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("multipart/x-mixed-replace");
                Headers.ContentType.Parameters.Add(new System.Net.Http.Headers.NameValueHeaderValue("boundary", b));
                hdrPre = Encoding.ASCII.GetBytes($"--{b}\r\nContent-Type: image/jpeg\r\nContent-Length: ");
                tail = Encoding.ASCII.GetBytes($"--{b}--\r\n");
            }
            public void Push(byte[] jpg) => q.Add(jpg);
            public void Complete() => q.CompleteAdding();
            protected override bool TryComputeLength(out long l) { l = -1; return false; }
            protected override async Task SerializeToStreamAsync(Stream s, TransportContext _)
            {
                foreach (var jpg in q.GetConsumingEnumerable())
                {
                    await s.WriteAsync(hdrPre); await s.WriteAsync(Encoding.ASCII.GetBytes(jpg.Length.ToString()));
                    await s.WriteAsync(Encoding.ASCII.GetBytes("\r\n\r\n")); await s.WriteAsync(jpg); await s.WriteAsync(new byte[] { 13, 10 });
                }
                await s.WriteAsync(tail);
            }
        }
    }
}
