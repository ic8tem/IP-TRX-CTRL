using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using IpTrxCtrl.Net;

namespace IpTrxCtrl.UI
{
    /// <summary>
    /// Panadapter della radio: in alto lo spettro, sotto la cascata (scorre verso il basso), in fondo la scala
    /// delle frequenze. Linea rossa = frequenza di ricezione, fascia = filtro. Clic = sintonia sul punto,
    /// rotella = un passo di sintonia.
    /// </summary>
    public sealed class PanadapterView : Control
    {
        private const int Rows = 160;
        private const double AxisH = 18, SpecFrac = 0.34;
        private readonly ConcurrentQueue<SpectrumLine> _queue = new();
        private int[] _pixels = Array.Empty<int>();
        private int _cols, _head;
        private WriteableBitmap? _bmp;
        private readonly int[] _palette = BuildPalette();
        private byte[] _last = Array.Empty<byte>();
        private double[] _smooth = Array.Empty<double>();
        private long _start, _end;
        private bool _out;
        private double _hoverX = -1;
        private bool _errorLogged;

        /// <summary>Frequenza di ricezione (VFO A).</summary>
        public long DialHz { get; set; }
        /// <summary>Larghezza del filtro (Hz) e modo, per la fascia del filtro.</summary>
        public int PassbandHz { get; set; }
        public string Mode { get; set; } = "";
        /// <summary>Intensità della cascata 0..100.</summary>
        public double Intensity { get; set; } = 50;
        public event Action<long>? FrequencyClicked;
        public event Action<int>? WheelSteps;

        public PanadapterView()
        {
            ClipToBounds = true;
            Cursor = new Cursor(StandardCursorType.Cross);
        }

        private static int[] BuildPalette()
        {
            var stops = new (double p, byte r, byte g, byte b)[] { (0, 4, 14, 40), (0.35, 10, 60, 140), (0.6, 30, 170, 200), (0.8, 250, 220, 60), (1, 255, 60, 30) };
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

        /// <summary>Da qualunque thread.</summary>
        public void Push(SpectrumLine l)
        {
            if (_queue.Count < 64) _queue.Enqueue(l);
        }

        public void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
            _cols = 0; _pixels = Array.Empty<int>(); _last = Array.Empty<byte>(); _bmp = null;
            InvalidateVisual();
        }

        /// <summary>Dal timer dell'interfaccia.</summary>
        public void Flush()
        {
            bool any = false;
            while (_queue.TryDequeue(out var l))
            {
                any = true;
                _out = l.OutOfRange;
                if (l.OutOfRange || l.Bins.Length == 0) continue;
                if (l.Bins.Length != _cols || l.StartHz != _start || l.EndHz != _end)
                {
                    // ampiezza o centro cambiati: la cascata riparte
                    bool sizeChanged = l.Bins.Length != _cols;
                    _cols = l.Bins.Length;
                    _start = l.StartHz; _end = l.EndHz;
                    if (sizeChanged || _pixels.Length != _cols * Rows) { _pixels = new int[_cols * Rows]; _bmp = null; _smooth = new double[_cols]; }
                    Array.Fill(_pixels, _palette[0]);
                    _head = 0;
                }
                _last = l.Bins;
                // la riga più nuova va in alto: buffer circolare di righe
                _head = (_head - 1 + Rows) % Rows;
                double gain = 0.6 + Intensity / 100.0 * 1.2;   // da 0,6 a 1,8
                // fondo: mediana della riga
                var copy = (byte[])l.Bins.Clone();
                Array.Sort(copy);
                double floor = copy[copy.Length / 2];
                int row = _head * _cols;
                for (int x = 0; x < _cols; x++)
                {
                    double v = (l.Bins[x] - floor) * gain * 2.2 + 40;
                    _pixels[row + x] = _palette[(int)Math.Clamp(v, 0, 255)];
                    _smooth[x] = _smooth[x] * 0.4 + l.Bins[x] * 0.6;
                }
            }
            if (any) InvalidateVisual();
        }

        /// <summary>kHz con la virgola decimale (la globalizzazione del programma è invariante: niente culture).</summary>
        private static string Khz(long hz) => (hz / 1000.0).ToString("0.0", CultureInfo.InvariantCulture).Replace('.', ',');

        private double XOf(long hz, double w) => _end > _start ? (hz - _start) / (double)(_end - _start) * w : -1;
        private long HzAt(double x, double w) => _end > _start ? _start + (long)Math.Round(x / Math.Max(1, w) * (_end - _start)) : 0;

        public override void Render(DrawingContext context)
        {
            if (Bounds.Width < 60 || Bounds.Height < 60) return;
            try { RenderCore(context); }
            catch (Exception ex)
            {
                if (!_errorLogged) { _errorLogged = true; Program.LogError("Panadapter: " + ex); }
            }
        }

        private void RenderCore(DrawingContext context)
        {
            double w = Bounds.Width, h = Bounds.Height - AxisH;
            double specH = Math.Round(h * SpecFrac), wfY = specH, wfH = h - specH;
            var bg = new SolidColorBrush(Color.FromRgb(4, 14, 40));
            context.FillRectangle(bg, new Rect(0, 0, w, Bounds.Height));
            var muted = Application.Current?.FindResource("B.Muted") as IBrush ?? Brushes.Gray;
            var tf = new Typeface("Consolas, Menlo, DejaVu Sans Mono, monospace");

            if (_cols == 0 || _end <= _start)
            {
                string msg = _out ? "Frequenza fuori dal campo dello scopio della radio" : "In attesa dei dati del panadapter…";
                var ft = new FormattedText(msg, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 12, Brushes.LightGray);
                context.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
                return;
            }

            // cascata
            _bmp ??= new WriteableBitmap(new PixelSize(_cols, Rows), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = _bmp.Lock())
            {
                for (int y = 0; y < Rows; y++)
                {
                    int src = ((_head + y) % Rows) * _cols;
                    Marshal.Copy(_pixels, src, fb.Address + y * fb.RowBytes, _cols);
                }
            }
            context.DrawImage(_bmp, new Rect(0, 0, _cols, Rows), new Rect(0, wfY, w, wfH));

            // fascia del filtro
            if (DialHz > 0 && PassbandHz > 0)
            {
                long lo = DialHz, hi = DialHz;
                string m = Mode.ToUpperInvariant();
                if (m.StartsWith("LSB") || m == "PKTLSB" || m == "CWR" || m == "RTTYR") lo = DialHz - PassbandHz;
                else if (m.StartsWith("CW") || m.StartsWith("AM") || m.StartsWith("FM") || m == "DSB") { lo = DialHz - PassbandHz / 2; hi = DialHz + PassbandHz / 2; }
                else hi = DialHz + PassbandHz;
                double x0 = XOf(lo, w), x1 = XOf(hi, w);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), new Rect(Math.Min(x0, x1), 0, Math.Max(1, Math.Abs(x1 - x0)), h));
            }

            // spettro
            if (_smooth.Length == _cols && _cols > 1)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    g.BeginFigure(new Point(0, specH), true);
                    for (int x = 0; x < _cols; x++)
                    {
                        double px = x / (double)(_cols - 1) * w;
                        double py = specH - _smooth[x] / 255.0 * (specH - 4);
                        g.LineTo(new Point(px, py));
                    }
                    g.LineTo(new Point(w, specH));
                    g.EndFigure(true);
                }
                context.DrawGeometry(new SolidColorBrush(Color.FromArgb(70, 120, 200, 255)), new Pen(new SolidColorBrush(Color.FromRgb(150, 220, 255)), 1), geo);
            }
            context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), 1), new Point(0, specH), new Point(w, specH));

            // frequenza di ricezione
            if (DialHz > 0)
            {
                double xd = XOf(DialHz, w);
                if (xd >= 0 && xd <= w) context.DrawLine(new Pen(Brushes.Red, 1.5), new Point(xd, 0), new Point(xd, h));
            }

            // scala delle frequenze
            double spanKhz = (_end - _start) / 1000.0;
            double step = 1;
            foreach (double st in new double[] { 0.5, 1, 2, 2.5, 5, 10, 20, 25, 50, 100, 200, 250, 500, 1000 })
                if (w / (spanKhz / st) >= 70) { step = st; break; }
            long stepHz = (long)(step * 1000);
            long first = (long)Math.Ceiling(_start / (double)stepHz) * stepHz;
            var tickPen = new Pen(new SolidColorBrush(Color.FromArgb(60, 255, 255, 255)), 1);
            for (long f = first; f <= _end; f += stepHz)
            {
                double x = XOf(f, w);
                context.DrawLine(tickPen, new Point(x, 0), new Point(x, specH));
                var ft = new FormattedText(Khz(f), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 10, muted);
                context.DrawText(ft, new Point(Math.Clamp(x - ft.Width / 2, 0, w - ft.Width), h + 2));
            }

            // sotto il mouse
            if (_hoverX >= 0 && _hoverX <= w)
            {
                long f = HzAt(_hoverX, w);
                context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1), new Point(_hoverX, 0), new Point(_hoverX, h));
                var ft = new FormattedText(Khz(f) + " kHz", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 12, Brushes.White);
                double tx = _hoverX + 8 + ft.Width > w ? _hoverX - ft.Width - 8 : _hoverX + 8;
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), new Rect(tx - 4, 2, ft.Width + 8, ft.Height + 2));
                context.DrawText(ft, new Point(tx, 3));
            }
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            _hoverX = e.GetPosition(this).X;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hoverX = -1;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            long f = HzAt(e.GetPosition(this).X, Bounds.Width);
            if (f > 0) FrequencyClicked?.Invoke(f);
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
        {
            base.OnPointerWheelChanged(e);
            int steps = Math.Sign(e.Delta.Y);
            if (steps != 0) WheelSteps?.Invoke(steps);
            e.Handled = true;
        }
    }
}
