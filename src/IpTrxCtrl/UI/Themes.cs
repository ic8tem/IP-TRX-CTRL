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

    /// <summary>I sei temi: Chiaro, Notte, Nero, Blu, Viola, Verde.</summary>
    public static class Themes
    {
        public static readonly List<Theme> All = new()
        {
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

        public static Theme Current { get; private set; } = All[0];

        public static Theme Get(string key) => All.Find(t => t.Key == key) ?? All[0];

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
