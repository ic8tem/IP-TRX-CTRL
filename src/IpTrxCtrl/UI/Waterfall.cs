using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IpTrxCtrl.Audio;

namespace IpTrxCtrl.UI
{
    /// <summary>
    /// Cascata 0-3000 Hz: tempo in orizzontale (scorre verso sinistra), frequenza in verticale.
    /// Evidenzia la fascia del pitch (200-1000 Hz), la linea del pitch e i segnali aperti in altre finestre.
    /// </summary>
    public sealed class Waterfall : Control
    {
        private const int Cols = 640;
        private static readonly int Rows = Spectrum.Bins;
        private const double AxisW = 48;
        private readonly int[] _pixels = new int[Cols * Rows];
        private WriteableBitmap? _bmp;
        private readonly ConcurrentQueue<float[]> _queue = new();
        private readonly int[] _palette = BuildPalette();
        private double _floor = -100;

        public double PitchHz { get; set; } = 600;
        public List<(double hz, IBrush brush)> Markers { get; } = new();
        public event Action<double>? FrequencyClicked;

        public Waterfall()
        {
            Array.Fill(_pixels, _palette[0]);
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Cross);
        }

        private static int[] BuildPalette()
        {
            // blu notte -> verde acqua -> azzurro -> bianco
            var stops = new (double p, byte r, byte g, byte b)[] { (0, 6, 41, 58), (0.45, 18, 92, 110), (0.7, 70, 190, 210), (0.88, 142, 235, 245), (1, 255, 255, 255) };
            var pal = new int[256];
            for (int i = 0; i < 256; i++)
            {
                double x = i / 255.0;
                int k = 0;
                while (k < stops.Length - 2 && x > stops[k + 1].p) k++;
                var a = stops[k]; var b = stops[k + 1];
                double t = Math.Clamp((x - a.p) / (b.p - a.p), 0, 1);
                byte r = (byte)(a.r + (b.r - a.r) * t), g = (byte)(a.g + (b.g - a.g) * t), bl = (byte)(a.b + (b.b - a.b) * t);
                pal[i] = unchecked((int)0xFF000000) | (r << 16) | (g << 8) | bl;
            }
            return pal;
        }

        /// <summary>Chiamato dal thread audio.</summary>
        public void Push(float[] row)
        {
            if (_queue.Count < 64) _queue.Enqueue(row);
        }

        /// <summary>Chiamato dal timer dell'interfaccia: aggiunge le righe arrivate e ridisegna.</summary>
        public void Flush()
        {
            bool any = false;
            while (_queue.TryDequeue(out var row))
            {
                any = true;
                // livello del rumore: mediana della riga, seguita lentamente
                var copy = (float[])row.Clone();
                Array.Sort(copy);
                double med = copy[copy.Length / 2];
                _floor += (med - _floor) * 0.05;
                for (int y = 0; y < Rows; y++)
                {
                    int off = y * Cols;
                    Array.Copy(_pixels, off + 1, _pixels, off, Cols - 1);
                    int bin = Rows - 1 - y;   // in alto le frequenze alte
                    double v = (row[bin] - _floor - 3) / 35.0;
                    int idx = (int)Math.Clamp(v * 255, 0, 255);
                    _pixels[off + Cols - 1] = _palette[idx];
                }
            }
            if (any) InvalidateVisual();
        }

        private static double Y(double hz, double h) => (1 - hz / Spectrum.MaxHz) * h;

        public override void Render(DrawingContext context)
        {
            double w = Math.Max(10, Bounds.Width - AxisW), h = Bounds.Height;
            _bmp ??= new WriteableBitmap(new PixelSize(Cols, Rows), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = _bmp.Lock())
            {
                for (int y = 0; y < Rows; y++)
                    Marshal.Copy(_pixels, y * Cols, fb.Address + y * fb.RowBytes, Cols);
            }
            var dest = new Rect(0, 0, w, h);
            context.DrawImage(_bmp, new Rect(0, 0, Cols, Rows), dest);

            // fascia del pitch e linee
            double y1000 = Y(1000, h), y200 = Y(200, h);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(26, 124, 242, 154)), new Rect(0, y1000, w, y200 - y1000));
            var green = new Pen(new SolidColorBrush(Color.FromRgb(124, 242, 154)), 1, new DashStyle(new double[] { 6, 4 }, 0));
            double yp = Y(PitchHz, h);
            context.DrawLine(green, new Point(0, yp), new Point(w, yp));
            foreach (var (hz, brush) in Markers)
            {
                double ym = Y(hz, h);
                context.DrawLine(new Pen(brush, 1, new DashStyle(new double[] { 6, 4 }, 0)), new Point(0, ym), new Point(w, ym));
            }

            // asse delle frequenze
            var axisBrush = Application.Current?.FindResource("B.Muted") as IBrush ?? Brushes.Gray;
            var tf = new Typeface("Consolas, Menlo, DejaVu Sans Mono, monospace");
            foreach (int f in new[] { 3000, 2500, 2000, 1500, 1000, 500, 0 })
            {
                var ft = new FormattedText(f.ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, axisBrush);
                double y = Math.Clamp(Y(f, h) - ft.Height / 2, 0, h - ft.Height);
                context.DrawText(ft, new Point(w + 14, y));
            }
            var arrow = new StreamGeometry();
            using (var g = arrow.Open())
            {
                g.BeginFigure(new Point(w + 2, yp), true);
                g.LineTo(new Point(w + 12, yp - 7));
                g.LineTo(new Point(w + 12, yp + 7));
                g.EndFigure(true);
            }
            context.DrawGeometry(new SolidColorBrush(Color.FromRgb(34, 164, 71)), null, arrow);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var p = e.GetPosition(this);
            double w = Math.Max(10, Bounds.Width - AxisW);
            if (p.X > w) return;
            double hz = Math.Clamp((1 - p.Y / Bounds.Height) * Spectrum.MaxHz, 0, Spectrum.MaxHz);
            FrequencyClicked?.Invoke(Math.Round(hz / 10) * 10);
        }
    }
}
