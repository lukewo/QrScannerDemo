using System;
using System.Collections.Generic;
using System.Diagnostics;
using Android.Gms.Tasks;
using Android.Graphics;
using Android.Runtime;
using AndroidX.Camera.Core;
using Xamarin.Google.MLKit.Vision.BarCode;
using Xamarin.Google.MLKit.Vision.Common;
using Barcode = Xamarin.Google.MLKit.Vision.Barcode.Common.Barcode;

namespace QrScannerDemo.Droid.Scanner
{
    /// <summary>A decoded code in upright image coordinates.</summary>
    public sealed class DetectedCode
    {
        public string Text;
        public int Format;
        public PointF[] Corners;     // upright image coordinates (may be null)
        public RectF Bounds;         // upright image coordinates
    }

    /// <summary>Result of analysing one frame.</summary>
    public sealed class FrameResult
    {
        public IReadOnlyList<DetectedCode> Codes;
        public int ImageWidth;       // upright (rotation already applied)
        public int ImageHeight;
        public long DecodeMs;
    }

    /// <summary>
    /// CameraX analyzer that feeds frames to ML Kit.
    ///
    /// Speed choices (from Google's ML Kit real-time guidance):
    ///  - frames stay on the Java side: the camera's Image goes straight into
    ///    InputImage.FromMediaImage; no pixel copy into .NET (ZXing.Net.Mobile copies
    ///    every frame into a managed byte[] and decodes in C#);
    ///  - one frame in flight at a time; CameraX's KEEP_ONLY_LATEST drops the rest;
    ///  - formats restricted to QR by default;
    ///  - the ImageProxy is closed as soon as ML Kit finishes, so the next frame
    ///    is delivered immediately.
    ///
    /// Runs on the analysis executor (a background thread). Callbacks are raised on
    /// that thread; the renderer marshals to the UI thread.
    /// </summary>
    public sealed class QrAnalyzer : Java.Lang.Object, ImageAnalysis.IAnalyzer
    {
        readonly Java.Util.Concurrent.IExecutor callbackExecutor;
        volatile IBarcodeScanner scanner;
        volatile bool enabled = true;

        public QrAnalyzer(Java.Util.Concurrent.IExecutor callbackExecutor)
        {
            this.callbackExecutor = callbackExecutor;
        }

        /// <summary>Raised for every analysed frame (with or without codes).</summary>
        public event Action<FrameResult> FrameAnalyzed;

        /// <summary>When false, frames are released immediately without decoding.</summary>
        public bool Enabled { get => enabled; set => enabled = value; }

        /// <summary>Swap the ML Kit scanner (e.g. formats or auto-zoom changed). The old one is closed.</summary>
        public void SetScanner(IBarcodeScanner newScanner)
        {
            var old = scanner;
            scanner = newScanner;
            old?.Close();
        }

        // ---- ImageAnalysis.Analyzer default methods ----
        // In Java these are interface *default* methods, so implementing them is optional.
        // Classic Xamarin's generated Java wrapper declares every interface method and
        // routes it to C#, so leaving them out crashes with AbstractMethodError
        // ("getDefaultTargetResolution") when CameraX binds the use case.
        // These return exactly what the Java defaults return.

        /// <summary>No preferred resolution: the ResolutionSelector on the use case decides.</summary>
        public Android.Util.Size DefaultTargetResolution => null;

        /// <summary>ImageAnalysis.COORDINATE_SYSTEM_ORIGINAL: results in the analysis image's own coordinates.</summary>
        public int TargetCoordinateSystem => ImageAnalysis.CoordinateSystemOriginal;

        /// <summary>Only called for non-original coordinate systems; nothing to do.</summary>
        public void UpdateTransform(Matrix matrix) { }

        public void Analyze(IImageProxy proxy)
        {
            var current = scanner;
            var mediaImage = proxy.Image;
            if (!enabled || current == null || mediaImage == null)
            {
                proxy.Close();
                return;
            }

            int rotation = proxy.ImageInfo.RotationDegrees;
            bool swap = rotation == 90 || rotation == 270;
            int uprightW = swap ? proxy.Height : proxy.Width;
            int uprightH = swap ? proxy.Width : proxy.Height;

            InputImage input;
            try
            {
                input = InputImage.FromMediaImage(mediaImage, rotation);
            }
            catch
            {
                proxy.Close();
                return;
            }

            var stopwatch = Stopwatch.StartNew();
            current.Process(input)
                .AddOnCompleteListener(callbackExecutor, new CompleteListener(task =>
                {
                    stopwatch.Stop();
                    try
                    {
                        if (task.IsSuccessful)
                        {
                            FrameAnalyzed?.Invoke(new FrameResult
                            {
                                Codes = ReadCodes(task.Result),
                                ImageWidth = uprightW,
                                ImageHeight = uprightH,
                                DecodeMs = stopwatch.ElapsedMilliseconds,
                            });
                        }
                    }
                    finally
                    {
                        proxy.Close();   // releases the frame so CameraX can deliver the next one
                    }
                }));
        }

        static IReadOnlyList<DetectedCode> ReadCodes(Java.Lang.Object result)
        {
            var codes = new List<DetectedCode>();
            var list = result?.JavaCast<Java.Util.IList>();
            if (list == null) return codes;

            for (int i = 0; i < list.Size(); i++)
            {
                var barcode = list.Get(i)?.JavaCast<Barcode>();
                string text = barcode?.RawValue;
                if (string.IsNullOrEmpty(text)) continue;

                var box = barcode.BoundingBox;
                var corners = barcode.GetCornerPoints();
                codes.Add(new DetectedCode
                {
                    Text = text,
                    Format = barcode.Format,
                    Bounds = box == null ? null : new RectF(box),
                    Corners = corners == null ? null : Array.ConvertAll(corners, p => new PointF(p.X, p.Y)),
                });
            }
            return codes;
        }

        sealed class CompleteListener : Java.Lang.Object, IOnCompleteListener
        {
            readonly Action<Task> onComplete;
            public CompleteListener(Action<Task> onComplete) => this.onComplete = onComplete;
            public void OnComplete(Task task) => onComplete(task);
        }

        /// <summary>ML Kit format constant to a readable name.</summary>
        public static string FormatName(int format)
        {
            switch (format)
            {
                case Barcode.FormatQrCode: return "QR_CODE";
                case Barcode.FormatAztec: return "AZTEC";
                case Barcode.FormatDataMatrix: return "DATA_MATRIX";
                case Barcode.FormatPdf417: return "PDF417";
                case Barcode.FormatCode128: return "CODE_128";
                case Barcode.FormatCode39: return "CODE_39";
                case Barcode.FormatCode93: return "CODE_93";
                case Barcode.FormatCodabar: return "CODABAR";
                case Barcode.FormatEan13: return "EAN_13";
                case Barcode.FormatEan8: return "EAN_8";
                case Barcode.FormatItf: return "ITF";
                case Barcode.FormatUpcA: return "UPC_A";
                case Barcode.FormatUpcE: return "UPC_E";
                default: return "UNKNOWN";
            }
        }
    }
}
