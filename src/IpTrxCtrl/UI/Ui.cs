using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace IpTrxCtrl.UI
{
    /// <summary>Piccoli costruttori per le finestre secondarie scritte in codice.</summary>
    internal static class Ui
    {
        public static TextBlock H1(string text) => new() { Text = text, FontSize = 20, FontWeight = FontWeight.Bold };

        public static TextBlock H2(string text)
        {
            var t = new TextBlock { Text = text };
            t.Classes.Add("h2");
            return t;
        }

        public static TextBlock Sub(string text, double size = 13)
        {
            var t = new TextBlock { Text = text, FontSize = size, TextWrapping = TextWrapping.Wrap };
            t.Classes.Add("sub");
            return t;
        }

        public static TextBlock Lbl(string text)
        {
            var t = new TextBlock { Text = text };
            t.Classes.Add("lbl");
            return t;
        }

        public static TextBlock Mono(string text, double size = 13)
        {
            var t = new TextBlock { Text = text, FontSize = size };
            t.Classes.Add("mono");
            return t;
        }

        public static Button Btn(string text, bool accent = false)
        {
            var b = new Button { Content = text };
            if (accent) b.Classes.Add("accent");
            return b;
        }

        public static Border Panel(Control child)
        {
            var b = new Border { Child = child };
            b.Classes.Add("panel");
            return b;
        }

        public static Border Inset(Control child)
        {
            var b = new Border { Child = child };
            b.Classes.Add("inset");
            return b;
        }

        public static StackPanel Row(double spacing, params Control[] children)
        {
            var p = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
            foreach (var c in children) p.Children.Add(c);
            return p;
        }

        public static StackPanel Col(double spacing, params Control[] children)
        {
            var p = new StackPanel { Spacing = spacing };
            foreach (var c in children) p.Children.Add(c);
            return p;
        }

        public static Window Dialog(string title, double width)
        {
            return new Window
            {
                Title = title,
                Width = width,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
        }

        public static Thickness M(double l, double t, double r, double b) => new(l, t, r, b);
    }
}
