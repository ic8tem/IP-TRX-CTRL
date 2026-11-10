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
    /// Cascata 0-2000 Hz: tempo in orizzontale (scorre verso sinistra), frequenza in verticale.
    /// A sinistra la QRG reale di sintonia, a destra lo scostamento dal pitch (000 sul pitch).
    /// Evidenzia la fascia del pitch (200-1000 Hz), la linea del pitch e i segnali aperti in altre finestre.
    /// </summary>
    public sealed class Waterfall : Control
    {
        private const int Cols = 640;
        private static readonly int Rows = Spectrum.Bins;
        private const double AxisL = 84, AxisR = 48;   // a sinistra la QRG, a destra lo scostamento dal pitch
        private readonly int[] _pixels = new int[Cols * Rows];
        private int _head;   // colonna più vecchia: la prossima riga dello spettro si scrive qui (buffer circolare)
        private WriteableBitmap? _bmp;
        private readonly ConcurrentQueue<float[]> _queue = new();
        private readonly int[] _palette = BuildPalette();
        private double _floor = -100;

        public double PitchHz { get; set; } = 600;
        /// <summary>Intensità 0..100: valori bassi scuriscono il rumore e fanno risaltare i segnali.</summary>
        public double Intensity { get; set; } = 50;
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
            if (_queue.Count < 256) _queue.Enqueue(row);
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
                // righe più frequenti: passo più piccolo, stessa velocità di adattamento al rumore di prima
                _floor += (med - _floor) * 0.05 * Spectrum.Hop / 512.0;
                double offset = 15 - Intensity * 0.3;              // da +15 dB (scura) a -15 dB (chiara) sopra il rumore
                for (int y = 0; y < Rows; y++)
                {
                    int bin = Rows - 1 - y;   // in alto le frequenze alte
                    double v = (row[bin] - _floor - offset) / 30.0;
                    int idx = (int)Math.Clamp(v * 255, 0, 255);
                    _pixels[y * Cols + _head] = _palette[idx];
                }
                _head = (_head + 1) % Cols;
            }
            if (any) InvalidateVisual();
        }

        private static double Y(double hz, double h) => (1 - hz / Spectrum.MaxHz) * h;
        private static double HzAt(double y, double h) => Math.Clamp((1 - y / h) * Spectrum.MaxHz, 0, Spectrum.MaxHz);

        /// <summary>QRG della radio in ricezione (0 = sconosciuta): a sinistra si legge la frequenza reale dei segnali.</summary>
        public long DialHz { get; set; }
        /// <summary>CW-R (banda laterale inferiore): la QRG scende quando il tono sale.</summary>
        public bool Reverse { get; set; }

        private double _hoverY = -1;

        /// <summary>Frequenza reale di un segnale che si sente a <paramref name="audioHz"/>: la radio in CW mostra la
        /// frequenza del segnale che si sente sul pitch.</summary>
        public long QrgAt(double audioHz) => DialHz <= 0 ? 0 : DialHz + (long)Math.Round((Reverse ? -1 : 1) * (audioHz - PitchHz));

        private static string FormatQrg(long hz) => $"{hz / 1_000_000}.{(hz / 1000) % 1000:D3}.{hz % 1000:D3}";
        private static string FormatOffset(double d)
        {
            int v = (int)Math.Round(d);
            return v == 0 ? "000" : (v > 0 ? "+" : "-") + Math.Abs(v).ToString(CultureInfo.InvariantCulture);
        }

        public override void Render(DrawingContext context)
        {
            // prima dell'impaginazione (o con la cascata nascosta) le dimensioni possono essere nulle
            if (Bounds.Width < AxisL + AxisR + 20 || Bounds.Height < 40) return;
            try { RenderCore(context); }
            catch (Exception ex)
            {
                if (!_errorLogged) { _errorLogged = true; Program.LogError("Cascata: " + ex); }
            }
        }

        private bool _errorLogged;

        // bordi dell'immagine calcolati a ogni disegno dalla larghezza delle scritte delle due scale
        private double _x0 = AxisL, _x1 = -1;

        private void RenderCore(DrawingContext context)
        {
            double h = Bounds.Height;

            // scale: a destra lo scostamento dal pitch (000 sul pitch), a sinistra la QRG reale di sintonia
            var axisBrush = Application.Current?.FindResource("B.Muted") as IBrush ?? Brushes.Gray;
            var pitchBrush = new SolidColorBrush(Color.FromRgb(34, 164, 71));
            var tf = new Typeface("Consolas, Menlo, DejaVu Sans Mono, monospace");
            double step = 500;
            foreach (double st in new double[] { 50, 100, 200, 250, 500 })
                if (st / Spectrum.MaxHz * h >= 18) { step = st; break; }
            int kMin = (int)Math.Ceiling((0 - PitchHz) / step), kMax = (int)Math.Floor((Spectrum.MaxHz - PitchHz) / step);
            var ticks = new List<(double y, FormattedText left, FormattedText right)>();
            double wl = 0, wr = 0;
            for (int k = kMin; k <= kMax; k++)
            {
                double f = PitchHz + k * step;
                var brush = k == 0 ? pitchBrush : axisBrush;
                var right = new FormattedText(FormatOffset(k * step), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, brush);
                string leftTxt = DialHz > 0 ? FormatQrg(QrgAt(f)) : f.ToString("0", CultureInfo.InvariantCulture) + " Hz";
                var left = new FormattedText(leftTxt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 11, brush);
                ticks.Add((Y(f, h), left, right));
                wl = Math.Max(wl, left.Width);
                wr = Math.Max(wr, right.Width);
            }
            // le scritte entrano sempre per intero (anche a 144 MHz e con caratteri più larghi)
            double x0 = Math.Max(AxisL, Math.Ceiling(wl) + 14);
            double x1 = Bounds.Width - Math.Max(AxisR, Math.Ceiling(wr) + 20);
            if (x1 - x0 < 20) x1 = x0 + 20;
            double w = x1 - x0;
            _x0 = x0; _x1 = x1;

            _bmp ??= new WriteableBitmap(new PixelSize(Cols, Rows), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
            using (var fb = _bmp.Lock())
            {
                // dalla colonna più vecchia (a sinistra) alla più nuova (a destra)
                int older = Cols - _head;
                for (int y = 0; y < Rows; y++)
                {
                    IntPtr line = fb.Address + y * fb.RowBytes;
                    Marshal.Copy(_pixels, y * Cols + _head, line, older);
                    if (_head > 0) Marshal.Copy(_pixels, y * Cols, line + older * 4, _head);
                }
            }
            context.DrawImage(_bmp, new Rect(0, 0, Cols, Rows), new Rect(x0, 0, w, h));

            // fascia del pitch (senza linea sul pitch: lo indicano la freccia e lo 000 a destra) e decodificatori in più
            double y1000 = Y(1000, h), y200 = Y(200, h), yp = Y(PitchHz, h);
            context.FillRectangle(new SolidColorBrush(Color.FromArgb(26, 124, 242, 154)), new Rect(x0, y1000, w, y200 - y1000));
            foreach (var (hz, brush) in Markers)
            {
                double ym = Y(hz, h);
                context.DrawLine(new Pen(brush, 1, new DashStyle(new double[] { 6, 4 }, 0)), new Point(x0, ym), new Point(x1, ym));
            }

            var tick = new Pen(axisBrush, 1);
            foreach (var (yt, left, right) in ticks)
            {
                context.DrawText(right, new Point(x1 + 16, Math.Clamp(yt - right.Height / 2, 0, h - right.Height)));
                context.DrawText(left, new Point(x0 - 8 - left.Width, Math.Clamp(yt - left.Height / 2, 0, h - left.Height)));
                context.DrawLine(tick, new Point(x0 - 4, yt), new Point(x0, yt));
            }
            var arrow = new StreamGeometry();
            using (var g = arrow.Open())
            {
                g.BeginFigure(new Point(x1 + 2, yp), true);
                g.LineTo(new Point(x1 + 12, yp - 7));
                g.LineTo(new Point(x1 + 12, yp + 7));
                g.EndFigure(true);
            }
            context.DrawGeometry(pitchBrush, null, arrow);

            // sotto il mouse: QRG precisa e scostamento dal pitch
            if (_hoverY >= 0 && _hoverY <= h)
            {
                double f = HzAt(_hoverY, h);
                context.DrawLine(new Pen(new SolidColorBrush(Color.FromArgb(160, 255, 255, 255)), 1), new Point(x0, _hoverY), new Point(x1, _hoverY));
                string txt = (DialHz > 0 ? FormatQrg(QrgAt(f)) + "  " : "") + FormatOffset(f - PitchHz) + " Hz";
                var ft = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, tf, 12, Brushes.White);
                double ty = _hoverY - ft.Height - 4 < 0 ? _hoverY + 4 : _hoverY - ft.Height - 4;
                var box = new Rect(x0 + 6, ty - 1, ft.Width + 10, ft.Height + 2);
                context.FillRectangle(new SolidColorBrush(Color.FromArgb(200, 0, 0, 0)), box);
                context.DrawText(ft, new Point(x0 + 11, ty));
            }
        }

        private bool InImage(Point p) => p.X >= _x0 && p.X <= (_x1 > 0 ? _x1 : Bounds.Width - AxisR);

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            base.OnPointerMoved(e);
            var p = e.GetPosition(this);
            _hoverY = InImage(p) ? p.Y : -1;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            base.OnPointerExited(e);
            _hoverY = -1;
            InvalidateVisual();
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            base.OnPointerPressed(e);
            var p = e.GetPosition(this);
            if (!InImage(p)) return;
            double hz = HzAt(p.Y, Bounds.Height);
            FrequencyClicked?.Invoke(Math.Round(hz / 10) * 10);
        }
    }
}
