using System;
using Android.Content;
using Android.Graphics;
using Android.OS;
using Android.Views;

namespace QrScannerDemo.Droid.Scanner
{
    /// <summary>
    /// Draws the scanner UI on top of the camera preview: a dimmed mask with a
    /// square cut-out, corner brackets, a sweeping scan line while scanning, and an
    /// outline around detected codes.
    ///
    /// This works because the PreviewView runs in COMPATIBLE mode (a TextureView).
    /// In PERFORMANCE mode it uses a SurfaceView, which punches through views
    /// drawn over it (the same issue we hit with the LiveKit video views).
    /// </summary>
    public sealed class ViewfinderOverlay : View
    {
        const float ViewfinderFraction = 0.68f;   // square side as a fraction of the shorter view side
        const long HighlightMs = 600;

        readonly float density;
        readonly Paint dimPaint, cornerPaint, linePaint, highlightPaint, highlightFill;
        readonly Path dimPath = new Path();   // even-odd fill: full rect minus the viewfinder hole
        readonly Path highlightPath = new Path();

        RectF viewfinder = new RectF();
        PointF[] highlight;           // view coordinates
        long highlightUntil;
        bool locked;                  // single-scan result shown: keep the outline, stop the line
        bool scanning = true;

        public ViewfinderOverlay(Context context) : base(context)
        {
            density = context.Resources.DisplayMetrics.Density;

            dimPaint = new Paint { Color = Color.Argb(150, 0, 0, 0), AntiAlias = true };
            cornerPaint = new Paint { Color = Color.White, StrokeWidth = 4 * density, AntiAlias = true, StrokeCap = Paint.Cap.Round };
            cornerPaint.SetStyle(Paint.Style.Stroke);
            linePaint = new Paint { Color = Color.Argb(200, 0, 200, 83), StrokeWidth = 2 * density, AntiAlias = true };
            highlightPaint = new Paint { Color = Color.Rgb(0, 200, 83), StrokeWidth = 4 * density, AntiAlias = true, StrokeJoin = Paint.Join.Round };
            highlightPaint.SetStyle(Paint.Style.Stroke);
            highlightFill = new Paint { Color = Color.Argb(60, 0, 200, 83), AntiAlias = true };
            dimPath.SetFillType(Path.FillType.EvenOdd);
        }

        /// <summary>The viewfinder square in this view's coordinates.</summary>
        public RectF Viewfinder => viewfinder;

        public void SetScanning(bool value)
        {
            scanning = value;
            if (value) locked = false;
            PostInvalidateOnAnimation();
        }

        /// <summary>Outline a detected code (view coordinates). <paramref name="hold"/> keeps it until scanning resumes.</summary>
        public void ShowHighlight(PointF[] corners, bool hold)
        {
            highlight = corners;
            highlightUntil = SystemClock.UptimeMillis() + HighlightMs;
            locked = hold;
            PostInvalidateOnAnimation();
        }

        protected override void OnSizeChanged(int w, int h, int oldw, int oldh)
        {
            base.OnSizeChanged(w, h, oldw, oldh);
            float side = Math.Min(w, h) * ViewfinderFraction;
            float left = (w - side) / 2f;
            float top = (h - side) / 2f - h * 0.06f;   // a little above centre, clear of the bottom panel
            viewfinder = new RectF(left, top, left + side, top + side);

            float r = 16 * density;
            dimPath.Reset();
            dimPath.AddRect(0, 0, w, h, Path.Direction.Cw);
            dimPath.AddRoundRect(viewfinder, r, r, Path.Direction.Cw);
        }

        protected override void OnDraw(Canvas canvas)
        {
            base.OnDraw(canvas);
            canvas.DrawPath(dimPath, dimPaint);
            DrawCorners(canvas);

            long now = SystemClock.UptimeMillis();
            bool showHighlight = highlight != null && (locked || now < highlightUntil);

            if (scanning && !locked)
            {
                // Sweeping line, 1.6 s per pass.
                float t = (now % 1600) / 1600f;
                float phase = t < 0.5f ? t * 2 : (1 - t) * 2;
                float inset = 12 * density;
                float y = viewfinder.Top + inset + phase * (viewfinder.Height() - 2 * inset);
                canvas.DrawLine(viewfinder.Left + inset, y, viewfinder.Right - inset, y, linePaint);
            }

            if (showHighlight)
            {
                highlightPath.Reset();
                highlightPath.MoveTo(highlight[0].X, highlight[0].Y);
                for (int i = 1; i < highlight.Length; i++) highlightPath.LineTo(highlight[i].X, highlight[i].Y);
                highlightPath.Close();
                canvas.DrawPath(highlightPath, highlightFill);
                canvas.DrawPath(highlightPath, highlightPaint);
            }

            if ((scanning && !locked) || (showHighlight && !locked))
                PostInvalidateOnAnimation();
        }

        void DrawCorners(Canvas canvas)
        {
            float len = 28 * density;
            var v = viewfinder;
            // top-left
            canvas.DrawLine(v.Left, v.Top + len, v.Left, v.Top, cornerPaint);
            canvas.DrawLine(v.Left, v.Top, v.Left + len, v.Top, cornerPaint);
            // top-right
            canvas.DrawLine(v.Right - len, v.Top, v.Right, v.Top, cornerPaint);
            canvas.DrawLine(v.Right, v.Top, v.Right, v.Top + len, cornerPaint);
            // bottom-left
            canvas.DrawLine(v.Left, v.Bottom - len, v.Left, v.Bottom, cornerPaint);
            canvas.DrawLine(v.Left, v.Bottom, v.Left + len, v.Bottom, cornerPaint);
            // bottom-right
            canvas.DrawLine(v.Right - len, v.Bottom, v.Right, v.Bottom, cornerPaint);
            canvas.DrawLine(v.Right, v.Bottom - len, v.Right, v.Bottom, cornerPaint);
        }
    }

    /// <summary>
    /// Maps upright analysis-image coordinates to PreviewView coordinates for
    /// ScaleType.FILL_CENTER (scale to cover, centre-crop). Valid because preview and
    /// analysis use the same aspect ratio (both requested as 16:9).
    /// </summary>
    public readonly struct ImageToViewMapper
    {
        readonly float scale, dx, dy;

        public ImageToViewMapper(int imageWidth, int imageHeight, int viewWidth, int viewHeight)
        {
            scale = Math.Max(viewWidth / (float)imageWidth, viewHeight / (float)imageHeight);
            dx = (viewWidth - imageWidth * scale) / 2f;
            dy = (viewHeight - imageHeight * scale) / 2f;
        }

        public PointF Map(PointF p) => new PointF(p.X * scale + dx, p.Y * scale + dy);
        public float Scale => scale;
    }
}
