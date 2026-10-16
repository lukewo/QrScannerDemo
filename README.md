# Fast native QR scanner for Xamarin.Forms (CameraX + ML Kit)

A classic Xamarin.Forms demo of a custom, fully native Android QR scanner, built as a faster replacement for ZXing.Net.Mobile. It uses only **official Microsoft NuGet bindings**: no custom binding library and no third-party packages.

- **Camera:** CameraX (`Xamarin.AndroidX.Camera.*` 1.3.3)
- **Decoder:** Google ML Kit barcode scanning, bundled model (`Xamarin.Google.MLKit.BarcodeScanning` 117.2.0.4). Works offline and without Google Play services.
- **Reusable control:** a `ScannerView` with bindable properties and events, rendered natively on Android.

## Why it's faster than ZXing.Net.Mobile

| | ZXing.Net.Mobile | This scanner |
|---|---|---|
| Decoder | Managed C# port of ZXing | ML Kit (native) |
| Frame path | Every frame copied into a .NET `byte[]` | Camera `Image` handed straight to ML Kit. Only the decoded string reaches .NET |
| Camera API | Deprecated Camera1, timer-driven autofocus | CameraX, continuous autofocus |
| Frame size | Often full preview resolution | ~1280×720 analysis stream |
| Backpressure | n/a | `KEEP_ONLY_LATEST`: one frame in flight, stale frames dropped |
| Formats | Many by default | QR only by default (toggle to all) |

## Features in the demo

- **Viewfinder overlay:** dimmed surroundings, corner brackets, a scan line, and an outline around the detected code. Drawn natively.
- **Region of interest:** only accept codes inside the viewfinder square (toggle).
- **Single or continuous mode.** Continuous mode suppresses repeats of the same code and keeps a history.
- **ML Kit auto-zoom:** zooms in when a code is detected but too small to read.
- **Manual zoom** (slider or pinch), **tap to focus**, **torch**.
- **Metrics:** time to scan per result, frames analysed per second, ML Kit milliseconds per frame.
- **Result card:** copy, or open the link. Haptic feedback on success.

## Projects

| Path | What it is |
|---|---|
| `QrScannerDemo/QrScannerDemo/` | Shared Forms code: `Controls/ScannerView`, `Models/ScanResult`, `MainPage` |
| `QrScannerDemo/QrScannerDemo.Android/Scanner/` | `ScannerViewRenderer` (CameraX + ML Kit), `QrAnalyzer`, `ViewfinderOverlay` |
| `tools/` | `build.ps1` (MSBuild helper), `smoke-test.ps1` (install + launch + log check), `list-types.ps1` (list C# API names in binding DLLs) |

## Requirements and running

- Visual Studio 2022 with the Xamarin workload, JDK 17, Android SDK platform 33
- Open `QrScannerDemo.sln`, set `QrScannerDemo.Android` as the startup project, and run on a device. The emulator's camera is only a virtual scene.

## Findings and gotchas (classic Xamarin)

1. **Last Xamarin.Android-compatible versions:** CameraX **1.3.3** and ML Kit **117.2.0.4** are the newest packages that still support classic Xamarin.Android (MonoAndroid). Later versions are .NET for Android only.
2. **AndroidX conflicts with Xamarin.Forms 5.** Forms 5.0.0.2662 caps `Xamarin.AndroidX.Lifecycle.LiveData` at ≤ 2.6.1.1, but CameraX needs ≥ 2.7.0.1. This fails restore with NU1107. Referencing LiveData 2.7.0.1 directly fixes it, leaving a harmless NU1608 warning. `RecyclerView` 1.2.1.5 is also referenced to avoid an NU1605 downgrade.
3. **Java default interface methods must be implemented in C#.** CameraX's `ImageAnalysis.Analyzer` has default methods (`getDefaultTargetResolution`, `getTargetCoordinateSystem`, `updateTransform`). Classic Xamarin's generated Java wrapper declares them all, so leaving them out crashes with `AbstractMethodError` when the camera binds. `QrAnalyzer` implements them with the Java default values.
4. **Use `PreviewView` in `COMPATIBLE` mode (TextureView).** The default PERFORMANCE mode uses a SurfaceView, which punches through overlays drawn on top of it.
5. **Use the same aspect ratio for preview and analysis** (both 16:9 here). Then ML Kit's coordinates map onto the screen with a simple scale and centre-crop, which keeps the region-of-interest check and the outline accurate.

## Status

- **Builds:** clean.
- **Emulator (Android 15):** the camera, preview, overlay and bundled ML Kit model all start, with no crashes.
- **Not yet confirmed:** decoding speed on a physical device.
