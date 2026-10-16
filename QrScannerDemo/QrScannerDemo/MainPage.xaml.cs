using System;
using System.Collections.ObjectModel;
using QrScannerDemo.Models;
using Xamarin.Essentials;
using Xamarin.Forms;

namespace QrScannerDemo
{
    public partial class MainPage : ContentPage
    {
        static readonly Color On = Color.FromHex("#00C853");
        static readonly Color Off = Color.FromHex("#33FFFFFF");

        readonly ObservableCollection<ScanResult> history = new ObservableCollection<ScanResult>();
        ScanResult lastResult;
        double lastZoomRatio = 1;

        public MainPage()
        {
            InitializeComponent();
            HistoryList.ItemsSource = history;
            Scanner.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Scanner.IsScanning)) UpdateButtons();
            };
            UpdateButtons();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            var status = await Permissions.CheckStatusAsync<Permissions.Camera>();
            if (status != PermissionStatus.Granted)
                status = await Permissions.RequestAsync<Permissions.Camera>();

            bool granted = status == PermissionStatus.Granted;
            PermissionPanel.IsVisible = !granted;
            Scanner.IsCameraEnabled = granted;
        }

        protected override void OnDisappearing()
        {
            Scanner.IsCameraEnabled = false;
            base.OnDisappearing();
        }

        // ------------------------------------------------------------ scanner events

        void OnBarcodeDetected(object sender, ScanResult result)
        {
            lastResult = result;
            ResultText.Text = result.Text;
            ResultMeta.Text = $"{result.Format}  ·  scanned in {result.TimeToScanMs} ms  ·  decode {result.DecodeMs} ms";
            ResultCard.IsVisible = true;
            OpenButton.IsVisible = result.IsUrl;

            if (Scanner.ContinuousScan)
            {
                history.Insert(0, result);
                HistoryHeader.Text = $"Scanned ({history.Count})";
            }

            UpdateButtons();
        }

        void OnStatsUpdated(object sender, ScannerStats stats)
        {
            lastZoomRatio = stats.ZoomRatio;
            ZoomLabel.Text = $"{stats.ZoomRatio:0.0}×";
            StatsLabel.Text = Scanner.IsScanning
                ? $"{stats.FramesPerSecond:0} frames/s analysed  ·  {stats.AverageDecodeMs:0} ms per frame"
                : "Paused";
        }

        async void OnCameraError(object sender, string message)
            => await DisplayAlert("Camera", message, "OK");

        // ------------------------------------------------------------------ buttons

        void OnScanAgainClicked(object sender, EventArgs e)
        {
            ResultCard.IsVisible = false;
            Scanner.IsScanning = true;
        }

        async void OnCopyClicked(object sender, EventArgs e)
        {
            if (lastResult == null) return;
            await Clipboard.SetTextAsync(lastResult.Text);
        }

        async void OnOpenClicked(object sender, EventArgs e)
        {
            if (lastResult?.IsUrl == true)
                await Launcher.OpenAsync(new Uri(lastResult.Text));
        }

        void OnTorchClicked(object sender, EventArgs e)
        {
            if (!Scanner.HasTorch)
            {
                DisplayAlert("Torch", "This camera has no torch.", "OK");
                return;
            }
            Scanner.IsTorchOn = !Scanner.IsTorchOn;
            UpdateButtons();
        }

        void OnAutoZoomClicked(object sender, EventArgs e)
        {
            Scanner.AutoZoom = !Scanner.AutoZoom;
            UpdateButtons();
        }

        void OnModeClicked(object sender, EventArgs e)
        {
            Scanner.ContinuousScan = !Scanner.ContinuousScan;
            HistoryPanel.IsVisible = Scanner.ContinuousScan;
            ResultCard.IsVisible = false;
            Scanner.IsScanning = true;
            UpdateButtons();
        }

        void OnFormatsClicked(object sender, EventArgs e)
        {
            Scanner.QrOnly = !Scanner.QrOnly;
            UpdateButtons();
        }

        void OnRegionClicked(object sender, EventArgs e)
        {
            Scanner.RestrictToViewfinder = !Scanner.RestrictToViewfinder;
            UpdateButtons();
        }

        void OnOpenSettingsClicked(object sender, EventArgs e) => AppInfo.ShowSettingsUI();

        void UpdateButtons()
        {
            Set(TorchButton, Scanner.IsTorchOn, "Torch: on", "Torch: off");
            Set(AutoZoomButton, Scanner.AutoZoom, "Auto-zoom: on", "Auto-zoom: off");
            Set(ModeButton, Scanner.ContinuousScan, "Mode: continuous", "Mode: single");
            Set(FormatsButton, Scanner.QrOnly, "Formats: QR only", "Formats: all");
            Set(RegionButton, Scanner.RestrictToViewfinder, "Region: frame only", "Region: full view");
            ScanAgainButton.IsVisible = !Scanner.ContinuousScan && !Scanner.IsScanning;
        }

        static void Set(Button button, bool on, string onText, string offText)
        {
            button.Text = on ? onText : offText;
            button.BackgroundColor = on ? On : Off;
        }
    }
}
