using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace IpTrxCtrl.UI
{
    /// <summary>Finestre di messaggio semplici (Avalonia non ne ha di pronte).</summary>
    public static class Dialogs
    {
        public static Task Info(Window owner, string text, string title = "IP-TRX-CTRL") => Show(owner, text, title, false);

        public static Task<bool> YesNo(Window owner, string text, string title = "IP-TRX-CTRL") => Show(owner, text, title, true);

        private static async Task<bool> Show(Window owner, string text, string title, bool yesNo)
        {
            bool result = false;
            var w = new Window
            {
                Title = title,
                Width = 460,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Margin = new Thickness(0, 18, 0, 0) };
            if (yesNo)
            {
                var yes = new Button { Content = "Sì", MinWidth = 90 };
                yes.Classes.Add("accent");
                yes.Click += (_, _) => { result = true; w.Close(); };
                var no = new Button { Content = "No", MinWidth = 90 };
                no.Click += (_, _) => { result = false; w.Close(); };
                buttons.Children.Add(yes);
                buttons.Children.Add(no);
            }
            else
            {
                var ok = new Button { Content = "OK", MinWidth = 90 };
                ok.Classes.Add("accent");
                ok.Click += (_, _) => w.Close();
                buttons.Children.Add(ok);
            }
            w.Content = new StackPanel
            {
                Margin = new Thickness(24),
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 14 },
                    buttons
                }
            };
            await w.ShowDialog(owner);
            return result;
        }

        public static async Task CopyText(Visual v, string text)
        {
            var clip = TopLevel.GetTopLevel(v)?.Clipboard;
            if (clip != null) await clip.SetTextAsync(text);
        }
    }
}
