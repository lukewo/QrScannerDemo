using System;
using System.ComponentModel;
using System.Threading;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.Camera.Core;
using AndroidX.Camera.Core.ResolutionSelector;
using AndroidX.Camera.Lifecycle;
using AndroidX.Camera.View;
using AndroidX.Core.Content;
using AndroidX.Lifecycle;
using Java.Util.Concurrent;
using QrScannerDemo.Controls;
using QrScannerDemo.Droid.Scanner;
using QrScannerDemo.Models;
using Xamarin.Forms;
using Xamarin.Forms.Platform.Android;
using Xamarin.Google.MLKit.Vision.BarCode;
using Barcode = Xamarin.Google.MLKit.Vision.Barcode.Common.Barcode;
using Size = Android.Util.Size;
using View = Android.Views.View;

[assembly: ExportRenderer(typeof(ScannerView), typeof(ScannerViewRenderer))]

namespace QrScannerDemo.Droid.Scanner
{
    /// <summary>
    /// Native scanner: CameraX Preview + ImageAnalysis bound to the activity's
    /// lifecycle (so the camera stops/starts with the app automatically), ML Kit
    /// barcode scanning, and a custom overlay.
    /// </summary>
    public class ScannerViewRenderer : ViewRenderer<ScannerView, FrameLayout>
    {
        const long RepeatSuppressMs = 1500;   // continuous mode: ignore the same value for this long

        readonly Handler mainHandler = new Handler(Looper.MainLooper);
        readonly IExecutorService analysisExecutor = Executors.NewSingleThreadExecutor();

        PreviewView previewView;
        ViewfinderOverlay overlay;
        QrAnalyzer analyzer;

        ProcessCameraProvider cameraProvider;
        Preview preview;
        ImageAnalysis analysis;
        ICamera camera;
        bool cameraStarting;

        long scanStartedAt;          // SystemClock.ElapsedRealtime when scanning started/resumed
        string lastText;
        long lastTextAt;
        bool syncingZoom;

        // Stats, written on the analysis thread and read on the UI thread.
        int statFrames;
        long statDecodeMs;
        long statWindowStart = SystemClock.ElapsedRealtime();
        readonly Java.Lang.Runnable statsTick;

        public ScannerViewRenderer(Context context) : base(context)
        {
            statsTick = new Java.Lang.Runnable(ReportStats);
        }

        // ------------------------------------------------------------------ setup

        protected override void OnElementChanged(ElementChangedEventArgs<ScannerView> e)
        {
            base.OnElementChanged(e);

            if (e.NewElement != null && Control == null)
            {
                SetNativeControl(CreateNativeView());
                analyzer = new QrAnalyzer(analysisExecutor);
                analyzer.FrameAnalyzed += OnFrameAnalyzed;
                analyzer.Enabled = e.NewElement.IsScanning;
                overlay.SetScanning(e.NewElement.IsScanning);
            }

            if (e.NewElement != null && e.NewElement.IsCameraEnabled)
                StartCamera();
        }

        FrameLayout CreateNativeView()
        {
            var root = new FrameLayout(Context);
            var match = new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

            previewView = new PreviewView(Context);
            // COMPATIBLE = TextureView, so our overlay and Forms views can draw on top.
            previewView.SetImplementationMode(PreviewView.ImplementationMode.Compatible);
            previewView.SetScaleType(PreviewView.ScaleType.FillCenter);
            root.AddView(previewView, match);

            overlay = new ViewfinderOverlay(Context);
            root.AddView(overlay, new FrameLayout.LayoutParams(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent));

            // Tap to focus, pinch to zoom.
            var tap = new GestureDetector(Context, new TapListener(this));
            var pinch = new ScaleGestureDetector(Context, new PinchListener(this));
            root.Touch += (s, args) =>
            {
                pinch.OnTouchEvent(args.Event);
                if (!pinch.IsInProgress) tap.OnTouchEvent(args.Event);
                args.Handled = true;
            };

            root.ContentDescription = "Camera viewfinder. Point at a QR code. Tap to focus, pinch to zoom.";
            return root;
        }

        protected override void OnElementPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            base.OnElementPropertyChanged(sender, e);
            if (Element == null || Control == null) return;

            if (e.PropertyName == ScannerView.IsCameraEnabledProperty.PropertyName)
            {
                if (Element.IsCameraEnabled) StartCamera(); else StopCamera();
            }
            else if (e.PropertyName == ScannerView.IsScanningProperty.PropertyName)
            {
                bool on = Element.IsScanning;
                analyzer.Enabled = on;
                overlay.SetScanning(on);
                if (on)
                {
                    scanStartedAt = SystemClock.ElapsedRealtime();
                    lastText = null;
                }
            }
            else if (e.PropertyName == ScannerView.IsTorchOnProperty.PropertyName)
            {
                ApplyTorch();
            }
            else if (e.PropertyName == ScannerView.ZoomProperty.PropertyName)
            {
                if (!syncingZoom) camera?.CameraControl.SetLinearZoom((float)Math.Max(0, Math.Min(1, Element.Zoom)));
            }
            else if (e.PropertyName == ScannerView.AutoZoomProperty.PropertyName ||
                     e.PropertyName == ScannerView.QrOnlyProperty.PropertyName)
            {
                RebuildScanner();
            }
        }

        // ----------------------------------------------------------------- camera

        void StartCamera()
        {
            if (camera != null || cameraStarting) return;
            if (!(Context is ILifecycleOwner lifecycleOwner))
            {
                Element?.RaiseCameraError("The camera needs an AndroidX activity (FormsAppCompatActivity).");
                return;
            }

            cameraStarting = true;
            scanStartedAt = SystemClock.ElapsedRealtime();

            var future = ProcessCameraProvider.GetInstance(Context);
            future.AddListener(new Java.Lang.Runnable(() =>
            {
                cameraStarting = false;
                if (Element == null || !Element.IsCameraEnabled) return;   // turned off/disposed while starting
                try
                {
                    cameraProvider = (ProcessCameraProvider)future.Get();
                    BindUseCases(lifecycleOwner);
                }
                catch (Exception ex)
                {
                    Element?.RaiseCameraError("Couldn't start the camera: " + ex.Message);
                }
            }), ContextCompat.GetMainExecutor(Context));
        }

        void BindUseCases(ILifecycleOwner owner)
        {
            // Preview and analysis share a 16:9 aspect ratio so analysis coordinates
            // map onto the preview with a simple scale + centre-crop.
            var aspect = AspectRatioStrategy.Ratio169FallbackAutoStrategy;

            preview = new Preview.Builder()
                .SetResolutionSelector(new ResolutionSelector.Builder()
                    .SetAspectRatioStrategy(aspect)
                    .Build())
                .Build();
            preview.SetSurfaceProvider(previewView.SurfaceProvider);

            // ~720p is plenty for QR and far cheaper than full sensor resolution.
            analysis = new ImageAnalysis.Builder()
                .SetResolutionSelector(new ResolutionSelector.Builder()
                    .SetAspectRatioStrategy(aspect)
                    .SetResolutionStrategy(new ResolutionStrategy(new Size(1280, 720),
                        ResolutionStrategy.FallbackRuleClosestHigherThenLower))
                    .Build())
                .SetBackpressureStrategy(ImageAnalysis.StrategyKeepOnlyLatest)
                .Build();
            analysis.SetAnalyzer(analysisExecutor, analyzer);

            cameraProvider.UnbindAll();
            camera = cameraProvider.BindToLifecycle(owner, CameraSelector.DefaultBackCamera, preview, analysis);

            Element.SetHasTorch(camera.CameraInfo.HasFlashUnit);
            ApplyTorch();
            camera.CameraControl.SetLinearZoom((float)Element.Zoom);
            RebuildScanner();

            statWindowStart = SystemClock.ElapsedRealtime();
            mainHandler.RemoveCallbacks(statsTick);
            mainHandler.PostDelayed(statsTick, 1000);
        }

        void StopCamera()
        {
            mainHandler.RemoveCallbacks(statsTick);
            if (cameraProvider != null && preview != null && analysis != null)
                cameraProvider.Unbind(preview, analysis);
            analysis?.ClearAnalyzer();
            camera = null;
            preview = null;
            analysis = null;
        }

        void ApplyTorch()
        {
            if (camera != null && camera.CameraInfo.HasFlashUnit)
                camera.CameraControl.EnableTorch(Element.IsTorchOn);
        }

        IZoomState CurrentZoom => camera?.CameraInfo.ZoomState.Value?.JavaCast<IZoomState>();

        // ---------------------------------------------------------------- ML Kit

        void RebuildScanner()
        {
            if (Element == null || analyzer == null) return;

            var builder = new BarcodeScannerOptions.Builder();
            if (Element.QrOnly)
                builder.SetBarcodeFormats(Barcode.FormatQrCode, Array.Empty<int>());
            else
                builder.SetBarcodeFormats(Barcode.FormatAllFormats, Array.Empty<int>());

            // Auto-zoom: when ML Kit sees a code that's too small to decode, it asks us
            // to zoom in (up to the camera's max). Needs a bound camera.
            float maxZoom = CurrentZoom?.MaxZoomRatio ?? 1f;
            if (Element.AutoZoom && camera != null && maxZoom > 1f)
            {
                builder.SetZoomSuggestionOptions(
                    new ZoomSuggestionOptions.Builder(new ZoomCallback(this))
                        .SetMaxSupportedZoomRatio(maxZoom)
                        .Build());
            }

            analyzer.SetScanner(BarcodeScanning.GetClient(builder.Build()));
        }

        sealed class ZoomCallback : Java.Lang.Object, ZoomSuggestionOptions.IZoomCallback
        {
            readonly WeakReference<ScannerViewRenderer> owner;
            public ZoomCallback(ScannerViewRenderer r) => owner = new WeakReference<ScannerViewRenderer>(r);

            public bool SetZoom(float zoomRatio)
            {
                if (!owner.TryGetTarget(out var r) || r.camera == null) return false;
                r.camera.CameraControl.SetZoomRatio(zoomRatio);
                return true;
            }
        }

        // ---------------------------------------------------------------- results

        /// <summary>Analysis thread.</summary>
        void OnFrameAnalyzed(FrameResult frame)
        {
            Interlocked.Increment(ref statFrames);
            Interlocked.Add(ref statDecodeMs, frame.DecodeMs);

            if (frame.Codes.Count > 0)
                mainHandler.Post(() => HandleCodes(frame));
        }

        /// <summary>UI thread.</summary>
        void HandleCodes(FrameResult frame)
        {
            if (Element == null || previewView == null || !Element.IsScanning) return;

            var mapper = new ImageToViewMapper(frame.ImageWidth, frame.ImageHeight, previewView.Width, previewView.Height);
            var viewfinder = overlay.Viewfinder;

            foreach (var code in frame.Codes)
            {
                PointF[] corners = code.Corners != null && code.Corners.Length >= 4
                    ? Array.ConvertAll(code.Corners, mapper.Map)
                    : BoxCorners(code.Bounds, mapper);
                if (corners == null) continue;

                if (Element.RestrictToViewfinder)
                {
                    var c = Centre(corners);
                    if (!viewfinder.Contains(c.X, c.Y)) continue;
                }

                long now = SystemClock.ElapsedRealtime();
                bool continuous = Element.ContinuousScan;

                if (continuous && code.Text == lastText && now - lastTextAt < RepeatSuppressMs)
                {
                    lastTextAt = now;            // still in view: keep suppressing
                    overlay.ShowHighlight(corners, hold: false);
                    return;
                }

                long timeToScan = now - scanStartedAt;
                lastText = code.Text;
                lastTextAt = now;
                scanStartedAt = now;             // continuous: next result is timed from this one

                overlay.ShowHighlight(corners, hold: !continuous);
                if (!continuous)
                {
                    analyzer.Enabled = false;
                    ((IElementController)Element).SetValueFromRenderer(ScannerView.IsScanningProperty, false);
                    overlay.SetScanning(false);
                    overlay.ShowHighlight(corners, hold: true);
                }

                root().PerformHapticFeedback(FeedbackConstants.VirtualKey);
                Element.RaiseBarcodeDetected(new ScanResult(code.Text, QrAnalyzer.FormatName(code.Format), timeToScan, frame.DecodeMs));
                return;   // one result per frame
            }
        }

        View root() => Control;

        static PointF[] BoxCorners(RectF box, ImageToViewMapper mapper)
        {
            if (box == null) return null;
            return new[]
            {
                mapper.Map(new PointF(box.Left, box.Top)),
                mapper.Map(new PointF(box.Right, box.Top)),
                mapper.Map(new PointF(box.Right, box.Bottom)),
                mapper.Map(new PointF(box.Left, box.Bottom)),
            };
        }

        static PointF Centre(PointF[] pts)
        {
            float x = 0, y = 0;
            foreach (var p in pts) { x += p.X; y += p.Y; }
            return new PointF(x / pts.Length, y / pts.Length);
        }

        // ------------------------------------------------------------------ stats

        void ReportStats()
        {
            if (Element == null || camera == null) return;

            long now = SystemClock.ElapsedRealtime();
            double seconds = Math.Max(0.001, (now - statWindowStart) / 1000.0);
            int frames = Interlocked.Exchange(ref statFrames, 0);
            long decode = Interlocked.Exchange(ref statDecodeMs, 0);
            statWindowStart = now;

            var zoom = CurrentZoom;
            Element.RaiseStatsUpdated(new ScannerStats(frames / seconds, frames == 0 ? 0 : decode / (double)frames, zoom?.ZoomRatio ?? 1));

            // Keep the bound Zoom property in step with pinch/auto-zoom.
            if (zoom != null && Math.Abs(zoom.LinearZoom - Element.Zoom) > 0.01)
            {
                syncingZoom = true;
                ((IElementController)Element).SetValueFromRenderer(ScannerView.ZoomProperty, (double)zoom.LinearZoom);
                syncingZoom = false;
            }

            mainHandler.PostDelayed(statsTick, 1000);
        }

        // --------------------------------------------------------------- gestures

        void FocusAt(float x, float y)
        {
            if (camera == null) return;
            var point = previewView.MeteringPointFactory.CreatePoint(x, y);
            var action = new FocusMeteringAction.Builder(point)
                .SetAutoCancelDuration(3, TimeUnit.Seconds)
                .Build();
            camera.CameraControl.StartFocusAndMetering(action);
        }

        void PinchZoom(float factor)
        {
            var zoom = CurrentZoom;
            if (camera == null || zoom == null) return;
            float ratio = Math.Max(zoom.MinZoomRatio, Math.Min(zoom.MaxZoomRatio, zoom.ZoomRatio * factor));
            camera.CameraControl.SetZoomRatio(ratio);
        }

        sealed class TapListener : GestureDetector.SimpleOnGestureListener
        {
            readonly ScannerViewRenderer r;
            public TapListener(ScannerViewRenderer r) => this.r = r;
            public override bool OnSingleTapUp(MotionEvent e) { r.FocusAt(e.GetX(), e.GetY()); return true; }
        }

        sealed class PinchListener : ScaleGestureDetector.SimpleOnScaleGestureListener
        {
            readonly ScannerViewRenderer r;
            public PinchListener(ScannerViewRenderer r) => this.r = r;
            public override bool OnScale(ScaleGestureDetector detector) { r.PinchZoom(detector.ScaleFactor); return true; }
        }

        // ----------------------------------------------------------------- dispose

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                StopCamera();
                if (analyzer != null)
                {
                    analyzer.FrameAnalyzed -= OnFrameAnalyzed;
                    analyzer.SetScanner(null);
                }
                analysisExecutor.Shutdown();
            }
            base.Dispose(disposing);
        }
    }
}
