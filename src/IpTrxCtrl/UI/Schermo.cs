using System;
using Avalonia;
using Avalonia.Controls;

namespace IpTrxCtrl.UI
{
    /// <summary>
    /// Schermi piccoli (768p, 1366×768 o 1280×720; armonizzazione del 15/11/2026): la finestra principale, le altre finestre e i dialoghi
    /// non escono dall'area di lavoro. Un dialogo piu' alto dello schermo
    /// si accorcia e il contenuto scorre.
    /// </summary>
    internal static class Schermo
    {
        public static void Adatta(Window w, bool ingrandisciSeBasso = false)
        {
            w.Opened += (_, _) =>
            {
                try
                {
                    var sc = w.Screens.ScreenFromVisual(w) ?? w.Screens.Primary;
                    if (sc == null) return;
                    double k = sc.Scaling > 0 ? sc.Scaling : 1;
                    var wa = sc.WorkingArea;
                    double aw = wa.Width / k, ah = wa.Height / k - 40;   // 40: barra del titolo
                    if (w.WindowState != WindowState.Normal || aw <= 0 || ah <= 0) return;
                    if (ingrandisciSeBasso && (w.Height > ah || w.Width > aw)) { w.WindowState = WindowState.Maximized; return; }
                    bool cambiata = false;
                    if (w.SizeToContent == SizeToContent.Height || w.SizeToContent == SizeToContent.WidthAndHeight)
                    {
                        if (w.Bounds.Height > ah)
                        {
                            w.MaxHeight = ah;
                            if (w.Content is Control c && c is not ScrollViewer) { w.Content = null; w.Content = new ScrollViewer { Content = c }; }
                            cambiata = true;
                        }
                    }
                    else
                    {
                        if (w.MinHeight > ah) w.MinHeight = ah;
                        if (w.MinWidth > aw) w.MinWidth = aw;
                        if (w.Height > ah) { w.Height = ah; cambiata = true; }
                        if (w.Width > aw) { w.Width = aw; cambiata = true; }
                    }
                    if (cambiata)
                    {
                        double ww = Math.Min(w.Width > 0 && !double.IsNaN(w.Width) ? w.Width : w.Bounds.Width, aw);
                        w.Position = new PixelPoint(wa.X + (int)Math.Max(0, (wa.Width - ww * k) / 2), wa.Y + 4);
                    }
                }
                catch { }
            };
        }
    }
}
