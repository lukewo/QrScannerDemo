using System;
using System.ComponentModel;
using QrScannerDemo.Models;
using Xamarin.Forms;

namespace QrScannerDemo.Controls
{
    /// <summary>
    /// Live camera QR/barcode scanner. Rendered natively on Android by
    /// ScannerViewRenderer (CameraX preview + ML Kit analysis + custom overlay).
    ///
    /// Events are raised on the UI thread.
    /// </summary>
    public class ScannerView : View
    {
        /// <summary>Camera on/off. Only set to true after camera permission is granted.</summary>
        public static readonly BindableProperty IsCameraEnabledProperty = BindableProperty.Create(
            nameof(IsCameraEnabled), typeof(bool), typeof(ScannerView), false);

        /// <summary>
        /// Decode frames. Turning it off keeps the preview live but stops decoding,
        /// so resuming is instant. In single-scan mode the renderer sets it to false
        /// after a successful scan.
        /// </summary>
        public static readonly BindableProperty IsScanningProperty = BindableProperty.Create(
            nameof(IsScanning), typeof(bool), typeof(ScannerView), true, BindingMode.TwoWay);

        /// <summary>Keep scanning after a hit (ignores repeats of the same value for a short time).</summary>
        public static readonly BindableProperty ContinuousScanProperty = BindableProperty.Create(
            nameof(ContinuousScan), typeof(bool), typeof(ScannerView), false);

        public static readonly BindableProperty IsTorchOnProperty = BindableProperty.Create(
            nameof(IsTorchOn), typeof(bool), typeof(ScannerView), false);

        /// <summary>Manual zoom, 0 (widest) to 1 (max). Pinch-to-zoom on the preview also works.</summary>
        public static readonly BindableProperty ZoomProperty = BindableProperty.Create(
            nameof(Zoom), typeof(double), typeof(ScannerView), 0.0, BindingMode.TwoWay);

        /// <summary>Let ML Kit zoom in automatically when a code is detected but too small to read.</summary>
        public static readonly BindableProperty AutoZoomProperty = BindableProperty.Create(
            nameof(AutoZoom), typeof(bool), typeof(ScannerView), true);

        /// <summary>Only decode QR codes (faster). When false, all ML Kit formats are enabled.</summary>
        public static readonly BindableProperty QrOnlyProperty = BindableProperty.Create(
            nameof(QrOnly), typeof(bool), typeof(ScannerView), true);

        /// <summary>Only accept codes whose centre is inside the on-screen viewfinder square.</summary>
        public static readonly BindableProperty RestrictToViewfinderProperty = BindableProperty.Create(
            nameof(RestrictToViewfinder), typeof(bool), typeof(ScannerView), true);

        public bool IsCameraEnabled { get => (bool)GetValue(IsCameraEnabledProperty); set => SetValue(IsCameraEnabledProperty, value); }
        public bool IsScanning { get => (bool)GetValue(IsScanningProperty); set => SetValue(IsScanningProperty, value); }
        public bool ContinuousScan { get => (bool)GetValue(ContinuousScanProperty); set => SetValue(ContinuousScanProperty, value); }
        public bool IsTorchOn { get => (bool)GetValue(IsTorchOnProperty); set => SetValue(IsTorchOnProperty, value); }
        public double Zoom { get => (double)GetValue(ZoomProperty); set => SetValue(ZoomProperty, value); }
        public bool AutoZoom { get => (bool)GetValue(AutoZoomProperty); set => SetValue(AutoZoomProperty, value); }
        public bool QrOnly { get => (bool)GetValue(QrOnlyProperty); set => SetValue(QrOnlyProperty, value); }
        public bool RestrictToViewfinder { get => (bool)GetValue(RestrictToViewfinderProperty); set => SetValue(RestrictToViewfinderProperty, value); }

        /// <summary>A code was read.</summary>
        public event EventHandler<ScanResult> BarcodeDetected;

        /// <summary>Roughly once a second while the camera runs.</summary>
        public event EventHandler<ScannerStats> StatsUpdated;

        /// <summary>The camera couldn't start or failed.</summary>
        public event EventHandler<string> CameraError;

        /// <summary>Whether the device has a torch. Valid after the camera starts.</summary>
        public bool HasTorch { get; private set; }

        // ---- called by platform renderers ----

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void RaiseBarcodeDetected(ScanResult result) => BarcodeDetected?.Invoke(this, result);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void RaiseStatsUpdated(ScannerStats stats) => StatsUpdated?.Invoke(this, stats);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void RaiseCameraError(string message) => CameraError?.Invoke(this, message);

        [EditorBrowsable(EditorBrowsableState.Never)]
        public void SetHasTorch(bool value) => HasTorch = value;
    }
}
