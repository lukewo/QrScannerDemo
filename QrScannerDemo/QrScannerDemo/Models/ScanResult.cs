using System;

namespace QrScannerDemo.Models
{
    public sealed class ScanResult
    {
        public ScanResult(string text, string format, long timeToScanMs, long decodeMs)
        {
            Text = text;
            Format = format;
            TimeToScanMs = timeToScanMs;
            DecodeMs = decodeMs;
            Timestamp = DateTime.Now;
        }

        /// <summary>Decoded value (ML Kit rawValue).</summary>
        public string Text { get; }

        /// <summary>e.g. "QR_CODE", "EAN_13".</summary>
        public string Format { get; }

        /// <summary>
        /// Milliseconds from when scanning started (or resumed) to this result:
        /// what the user experiences as "how fast was the scan".
        /// </summary>
        public long TimeToScanMs { get; }

        /// <summary>ML Kit processing time for the frame that produced this result.</summary>
        public long DecodeMs { get; }

        public DateTime Timestamp { get; }

        public bool IsUrl =>
            Uri.TryCreate(Text, UriKind.Absolute, out var uri) &&
            (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        // For list display.
        public string Summary => $"{Timestamp:HH:mm:ss}  ·  {TimeToScanMs} ms  ·  {Format}";
    }

    public sealed class ScannerStats
    {
        public ScannerStats(double framesPerSecond, double averageDecodeMs, double zoomRatio)
        {
            FramesPerSecond = framesPerSecond;
            AverageDecodeMs = averageDecodeMs;
            ZoomRatio = zoomRatio;
        }

        /// <summary>Frames ML Kit analysed per second (dropped frames don't count).</summary>
        public double FramesPerSecond { get; }

        /// <summary>Average ML Kit processing time per analysed frame.</summary>
        public double AverageDecodeMs { get; }

        public double ZoomRatio { get; }
    }
}
