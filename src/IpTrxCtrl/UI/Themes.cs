using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace IpTrxCtrl.UI
{
    public sealed record Theme(string Key, string Name, bool Dark,
        string Bg, string Panel, string Inset, string Border, string Field, string Text, string Muted,
        string Btn1, string Btn2, string BtnText, string BtnBorder, string Accent, string AccentText,
        string QrgBg, string Qrg, string Ok, string Bar, string Track, string Tx, string TxDim,
        string LogBg, string LogText, string LogSend, string LogRecv, string LogErr, string LogMuted);

    /// <summary>
    /// I 18 temi, come E-LAYER ANALYZER (armonizzazione del 15/11/2026): Chiaro, Notte e Nero di YA-MS, gli otto pastelli
    /// (tinte di YAEC), Scia meteorica, poi i sei temi originali di IP-TRX-CTRL (Chiaro, Notte, Nero, Blu, Viola, Verde).
    /// Le chiavi dei sei originali non cambiano, quindi le impostazioni gia' salvate restano valide.
    /// </summary>
    public static class Themes
    {
        public static readonly List<Theme> All = new()
        {
            new("ms-chiaro", "Chiaro (YA-MS)", false, "#F0F0F0", "#FFFFFF", "#E6E8F0", "#D5DCEA", "#FFFFFF", "#000000", "#6D6D6D",
                "#FFFFFF", "#F0F0F0", "#000000", "#D5DCEA", "#1F6FD1", "#FFFFFF", "#1E293B", "#F5B942", "#139650", "#2E9E55", "#E1E4EB", "#B22222", "#E9C1C1",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("ms-notte", "Notte (YA-MS)", true, "#0B1220", "#121B2E", "#060B16", "#24314F", "#121B2E", "#DBE4F5", "#7F8DB0",
                "#333F57", "#1C2842", "#FFFFFF", "#495570", "#5FA8FF", "#0B1220", "#060B16", "#FFD166", "#3DDC84", "#30AA6B", "#1A2640", "#FF6E64", "#603238",
                "#060B16", "#CDD7EB", "#5FA8FF", "#3DDC84", "#FF9A92", "#7F8DB0"),
            new("ms-nero", "Nero (YA-MS)", true, "#000000", "#121212", "#0C0C0C", "#343434", "#121212", "#EBEBEB", "#8C8C8C",
                "#383838", "#202020", "#FFFFFF", "#595959", "#50B4FF", "#000000", "#0C0C0C", "#FFD148", "#3CEB82", "#2DB062", "#222222", "#FF5F55", "#59211E",
                "#0C0C0C", "#DDDDDD", "#50B4FF", "#3CEB82", "#FF8F88", "#8C8C8C"),
            new("azzurro", "Azzurro pastello", false, "#D3E6F9", "#F0F7FD", "#C0DBF6", "#C8E0F8", "#F6FAFE", "#000000", "#6D6D6D",
                "#F0F7FD", "#DEECFA", "#000000", "#C8E0F8", "#1F6FD1", "#FFFFFF", "#1E293B", "#F5B942", "#139650", "#2E9E55", "#D6E8FA", "#B22222", "#E9C1C1",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("arancio", "Arancio pastello", false, "#FBE3CE", "#FEF6EF", "#FAD8B9", "#FADCC2", "#FEF9F5", "#000000", "#6D6D6D",
                "#FEF6EF", "#FCEADA", "#000000", "#FADCC2", "#1F6FD1", "#FFFFFF", "#1E293B", "#F5B942", "#139650", "#2E9E55", "#FCE6D2", "#B22222", "#E9C1C1",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("acquamarina", "Acquamarina pastello", false, "#CBEDEB", "#EEF9F8", "#B6E6E2", "#BEE8E6", "#F4FBFB", "#000000", "#6D6D6D",
                "#EEF9F8", "#D8F2F0", "#000000", "#BEE8E6", "#1F6FD1", "#FFFFFF", "#1E293B", "#F5B942", "#139650", "#2E9E55", "#D0EEEC", "#B22222", "#E9C1C1",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("pverde", "Verde pastello", false, "#D5EBD7", "#F1F8F2", "#C4E2C7", "#CAE6CE", "#F6FBF7", "#000000", "#6D6D6D",
                "#F1F8F2", "#E0F0E1", "#000000", "#CAE6CE", "#1F6FD1", "#FFFFFF", "#1E293B", "#F5B942", "#139650", "#2E9E55", "#D8ECDB", "#B22222", "#E9C1C1",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("rosa", "Rosa pastello", false, "#F9DCE8", "#FDF3F7", "#F6CEDF", "#F8D4E2", "#FEF8FA", "#000000", "#6D6D6D",
                "#FDF3F7", "#FAE5EE", "#000000", "#F8D4E2", "#1F6FD1", "#FFFFFF", "#1E293B", "#F5B942", "#139650", "#2E9E55", "#FADFEA", "#B22222", "#E9C1C1",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("lilla", "Lilla pastello", false, "#E8DFF4", "#F7F4FB", "#DFD1F0", "#E2D6F2", "#FAF8FD", "#000000", "#6D6D6D",
                "#F7F4FB", "#EEE7F7", "#000000", "#E2D6F2", "#1F6FD1", "#FFFFFF", "#1E293B", "#F5B942", "#139650", "#2E9E55", "#EAE1F5", "#B22222", "#E9C1C1",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("indaco", "Indaco pastello", false, "#D5D7EF", "#F1F2FA", "#C4C7E9", "#CACEEC", "#F6F7FC", "#000000", "#6D6D6D",
                "#F1F2FA", "#E0E1F3", "#000000", "#CACEEC", "#1F6FD1", "#FFFFFF", "#1E293B", "#F5B942", "#139650", "#2E9E55", "#D8DBF1", "#B22222", "#E9C1C1",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("marrone", "Marrone pastello", false, "#E6DBD3", "#F7F3F0", "#DBCCC0", "#E0D2C8", "#FAF8F6", "#000000", "#6D6D6D",
                "#F7F3F0", "#ECE4DE", "#000000", "#E0D2C8", "#1F6FD1", "#FFFFFF", "#1E293B", "#F5B942", "#139650", "#2E9E55", "#E8DED6", "#B22222", "#E9C1C1",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("scia", "Scia meteorica", true, "#161230", "#1C1740", "#0E0B22", "#3A3270", "#161230", "#EEEBFA", "#B3ADD6",
                "#423972", "#2B2160", "#FFFFFF", "#5E578C", "#38BDF8", "#161230", "#0E0B22", "#FDE047", "#86EFAC", "#6AB88D", "#221C47", "#F87171", "#653347",
                "#0E0B22", "#E5E2F5", "#38BDF8", "#86EFAC", "#FA9C9C", "#B3ADD6"),
            new("chiaro", "Chiaro", false, "#F5F7FA", "#FFFFFF", "#F0F2F5", "#D5DAE1", "#FFFFFF", "#1B1F24", "#555D68",
                "#FDFEFF", "#E3E7ED", "#1B1F24", "#B9C0CA", "#1F6FEB", "#FFFFFF", "#141A22", "#F5B942", "#1E7A3C", "#2E9E55", "#DDE1E7", "#D92D20", "#F3C2BD",
                "#141A22", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#8C97A6"),
            new("notte", "Notte", true, "#0F1A2B", "#15233A", "#0B1526", "#2A3D5C", "#0B1526", "#E6ECF5", "#A9B6C9",
                "#2E4870", "#1F3354", "#FFFFFF", "#3B5682", "#5AA2FF", "#0B1526", "#070E19", "#F5B942", "#8EE0A6", "#4CC774", "#26364F", "#FF4D4F", "#5A2A33",
                "#070E19", "#D7DEE8", "#7FB3FF", "#8EE0A6", "#FF8A80", "#7E8CA3"),
            new("nero", "Nero", true, "#0B0B0B", "#151515", "#050505", "#303030", "#050505", "#F2F2F2", "#A6A6A6",
                "#323232", "#1D1D1D", "#FFFFFF", "#444444", "#FF9F1C", "#000000", "#000000", "#FFB000", "#7CE29A", "#3FBF6A", "#262626", "#FF3B30", "#4A1A17",
                "#000000", "#E0E0E0", "#FFC266", "#7CE29A", "#FF8A80", "#8A8A8A"),
            new("blu", "Blu", true, "#12417B", "#16498A", "#0C3162", "#2F64A8", "#0C3162", "#FFFFFF", "#C9D8EC",
                "#4384D3", "#2B63AD", "#FFFFFF", "#5A93DA", "#FFD24A", "#0C3162", "#0A2A55", "#FFD24A", "#9BF0B5", "#7FE0A0", "#0E3A70", "#FF5A52", "#6B2E4A",
                "#0A2A55", "#E6EEF8", "#FFD24A", "#9BF0B5", "#FFA39A", "#9DB4D3"),
            new("viola", "Viola", true, "#2A1A41", "#33204F", "#22143A", "#573A85", "#3C245D", "#F4EEFB", "#CDBFDF",
                "#7650AF", "#583789", "#FFFFFF", "#8A66C2", "#E2C6FF", "#2A1A41", "#1E1131", "#FFD98A", "#B9F2A0", "#9FE08A", "#45306A", "#FF5C7A", "#5E2A4E",
                "#1E1131", "#EDE4F8", "#E2C6FF", "#B9F2A0", "#FFA3B5", "#A897C2"),
            new("verde", "Verde", true, "#12301F", "#173B27", "#0B2215", "#2E6C49", "#0B2215", "#E6F6EC", "#BAD4C5",
                "#3E8A5D", "#2B6644", "#FFFFFF", "#4F9B6E", "#A6E35F", "#0B2215", "#08180F", "#E8F5A0", "#A6E35F", "#A6E35F", "#1F4A32", "#FF5A4E", "#4A2E25",
                "#08180F", "#E1F2E7", "#E8F5A0", "#A6E35F", "#FF9E94", "#86A893"),
        };

        public static Theme Current { get; private set; } = All.Find(t => t.Key == "chiaro")!;

        public static Theme Get(string key) => All.Find(t => t.Key == key) ?? All.Find(t => t.Key == "chiaro")!;

        public static Color C(string hex) => Color.Parse(hex);
        public static IBrush B(string hex) => new SolidColorBrush(Color.Parse(hex));

        public static void Apply(string key)
        {
            var t = Get(key);
            Current = t;
            var app = Application.Current;
            if (app == null) return;
            app.RequestedThemeVariant = t.Dark ? ThemeVariant.Dark : ThemeVariant.Light;
            var r = app.Resources;
            void S(string k, string hex) => r[k] = B(hex);
            S("B.Bg", t.Bg); S("B.Panel", t.Panel); S("B.Inset", t.Inset); S("B.Border", t.Border); S("B.Field", t.Field);
            S("B.Text", t.Text); S("B.Muted", t.Muted); S("B.BtnText", t.BtnText); S("B.BtnBorder", t.BtnBorder);
            S("B.BtnHover", t.Btn1); S("B.Accent", t.Accent); S("B.AccentText", t.AccentText);
            S("B.QrgBg", t.QrgBg); S("B.Qrg", t.Qrg); S("B.Ok", t.Ok); S("B.Bar", t.Bar); S("B.Track", t.Track);
            S("B.Tx", t.Tx); S("B.TxDim", t.TxDim);
            S("B.LogBg", t.LogBg); S("B.LogText", t.LogText); S("B.LogSend", t.LogSend); S("B.LogRecv", t.LogRecv);
            S("B.LogErr", t.LogErr); S("B.LogMuted", t.LogMuted);
            r["B.BtnBg"] = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative),
                GradientStops = { new GradientStop(C(t.Btn1), 0), new GradientStop(C(t.Btn2), 1) }
            };

            // colori dei controlli del tema Fluent
            r["SystemAccentColor"] = C(t.Accent);
            r["SystemAccentColorLight1"] = C(t.Accent);
            r["SystemAccentColorDark1"] = C(t.Accent);
            S("TextControlBackground", t.Field); S("TextControlBackgroundPointerOver", t.Field); S("TextControlBackgroundFocused", t.Field);
            S("TextControlForeground", t.Text); S("TextControlForegroundPointerOver", t.Text); S("TextControlForegroundFocused", t.Text);
            S("TextControlBorderBrush", t.Border); S("TextControlBorderBrushPointerOver", t.Accent); S("TextControlBorderBrushFocused", t.Accent);
            S("ComboBoxBackground", t.Field); S("ComboBoxBackgroundPointerOver", t.Field); S("ComboBoxBackgroundPressed", t.Inset);
            S("ComboBoxForeground", t.Text); S("ComboBoxBorderBrush", t.Border); S("ComboBoxBorderBrushPointerOver", t.Accent);
            S("ComboBoxDropDownBackground", t.Panel); S("ComboBoxItemForeground", t.Text); S("ComboBoxDropDownGlyphForeground", t.Muted);
            S("ComboBoxItemBackgroundPointerOver", t.Inset); S("ComboBoxItemBackgroundSelected", t.Inset); S("ComboBoxItemForegroundSelected", t.Text);
            S("SliderTrackFill", t.Track); S("SliderTrackFillPointerOver", t.Track); S("SliderTrackValueFill", t.Accent);
            S("SliderTrackValueFillPointerOver", t.Accent); S("SliderThumbBackground", t.Accent); S("SliderThumbBackgroundPointerOver", t.Accent);
            S("CheckBoxCheckBackgroundFillChecked", t.Accent); S("CheckBoxCheckBackgroundStrokeChecked", t.Accent); S("CheckBoxCheckGlyphForegroundChecked", t.AccentText);
            S("CheckBoxForegroundUnchecked", t.Text); S("CheckBoxForegroundChecked", t.Text);
        }
    }
}
