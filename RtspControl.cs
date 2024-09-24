using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using System.Threading;
using SimpleRtspPlayer.RawFramesDecoding.FFmpeg;
using SimpleRtspPlayer.RawFramesDecoding;
using RtspClientSharp;
using RtspClientSharp.RawFrames.Video;
using RtspClientSharp.Rtsp;
using Logging;

namespace RtspInfra {

    public class RtspControl {

        private readonly Dictionary<FFmpegVideoCodecId, FFmpegVideoDecoder> _videoDecodersMap = new Dictionary<FFmpegVideoCodecId, FFmpegVideoDecoder>();
        private Bitmap _bmp;
        private TransformParameters _transformParameters;
        private CancellationTokenSource _cancellationTokenSource;
        private Size _sensorSize;
        private Rectangle _sensorRect;

        public RtspControl() {
        }

        // RtspControl status
        public enum Status {
            UNDEFINED = -1,
            RUNNING = 0,
            CANCELLED = 1,
            EXCEPTION = 2,
        }
        private Status _status = Status.UNDEFINED;
        public Status GetStatus { 
            get {
                return _status;
            }
        }

        // error notification
        public delegate void ErrorHandler();
        public event ErrorHandler Error;
        // connect to a RTSP stream
        public async void StartRTSP(Size sensorSize, string url) {
            _status = Status.UNDEFINED;
            // camera sensor data
            _sensorSize = sensorSize;
            _sensorRect = new Rectangle(0, 0, sensorSize.Width, sensorSize.Height);
            // connection data
            var connectionParameters = new ConnectionParameters(new Uri(url));
            if ( _cancellationTokenSource != null ) {
                _cancellationTokenSource.Dispose();
                _cancellationTokenSource = null;
            }
            _cancellationTokenSource = new CancellationTokenSource();
            TimeSpan delay = TimeSpan.FromSeconds(10);
            using ( var rtspClient = new RtspClient(connectionParameters) ) {
                rtspClient.FrameReceived += RtspClient_FrameReceived;
                while ( true ) {
                    try {
                        _status = Status.RUNNING;
                        await rtspClient.ConnectAsync(_cancellationTokenSource.Token);
                        await rtspClient.ReceiveAsync(_cancellationTokenSource.Token);
                    } catch ( OperationCanceledException ) {
                        Logger.logTextLn(DateTime.Now, "RtspControl cancelled");
                        _status = Status.CANCELLED;
                        return;
                    } catch ( RtspClientException e ) {
                        Logger.logTextLnU(DateTime.Now, "RtspControl exception: " + e.Message);
                        _status = Status.EXCEPTION;
                        Error?.Invoke();
                        return;
                    }
                }
            }
        }

        // stop connection to a RTSP stream
        public void StopRTSP() {
            _cancellationTokenSource?.Cancel();
        }

        // tell a listener about a new frame; inspired by https://www.bytehide.com/blog/how-to-implement-events-in-csharp
        public delegate void NewFrameHandler(Bitmap bmp);
        public event NewFrameHandler NewFrame;
        // how to get a frame from a RTSP stream
        private void RtspClient_FrameReceived(object sender, RtspClientSharp.RawFrames.RawFrame e) {
            // sanity checks
            if ( _cancellationTokenSource == null || _cancellationTokenSource.IsCancellationRequested ) {
                return;
            }
            if ( !(e is RawVideoFrame rawVideoFrame) ) {
                return;
            }
            
            // select correct decoder
            var codecId = DetectCodecId(rawVideoFrame);
            if ( !_videoDecodersMap.TryGetValue(codecId, out FFmpegVideoDecoder decoder) ) {
                decoder = FFmpegVideoDecoder.CreateDecoder(codecId);
                _videoDecodersMap.Add(codecId, decoder);
            }
            var decodedVideoFrame = decoder.TryDecode(rawVideoFrame);

            // convert decoded frame to bmp
            if ( decodedVideoFrame != null ) {
                if ( _bmp != null ) {
                    _bmp.Dispose();
                }
 
                // new and empty bmp in the size of the native camera sensor
                _bmp = new Bitmap(_sensorSize.Width, _sensorSize.Height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                // lock the bitmap's bits
                var bmpData = _bmp.LockBits(_sensorRect, System.Drawing.Imaging.ImageLockMode.ReadWrite, _bmp.PixelFormat);

                // transform data
                IntPtr ptr = bmpData.Scan0;
                _transformParameters = new TransformParameters(
                    RectangleF.Empty,
                    _sensorSize,
                    ScalingPolicy.RespectAspectRatio,
                    PixelFormat.Bgra32, ScalingQuality.Nearest);
                decodedVideoFrame.TransformTo(ptr, bmpData.Stride, _transformParameters);

                try {
                    // unlock bitmap's bits
                    _bmp.UnlockBits(bmpData);

//// test output into _bmp
//using ( var graphics = Graphics.FromImage(_bmp) ) {
//    string text = rawVideoFrame.Timestamp.ToString("yyyy.MM.dd HH:mm:ss_fff", System.Globalization.CultureInfo.InvariantCulture);
//    graphics.DrawString(text, new Font("Arial", 20), Brushes.White, 0, 50);
//}

                    // raise event toward a listener 
                    NewFrame?.Invoke(_bmp);
                } catch ( System.InvalidOperationException ioe ) {
                    Logger.logTextLn(DateTime.Now, "RtspClient_FrameReceived exception: " + ioe.Message); 
                }
            }
        }

        // need to distinguish between H264 and MJPEG
        private FFmpegVideoCodecId DetectCodecId(RawVideoFrame videoFrame) {
            if ( videoFrame is RawJpegFrame )
                return FFmpegVideoCodecId.MJPEG;
            if ( videoFrame is RawH264Frame )
                return FFmpegVideoCodecId.H264;
            Logger.logTextLn(DateTime.Now, "DetectCodecId: " + nameof(videoFrame));
            throw new ArgumentOutOfRangeException(nameof(videoFrame));
        }

    }
}
