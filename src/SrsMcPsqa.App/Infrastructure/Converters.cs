using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SrsMcPsqa.Core.Models;
using SrsMcPsqa.Core.Services;

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>true → Visible, false → Collapsed (Invert swaps).</summary>
    public sealed class BoolToVisibilityConverter : IValueConverter
    {
        public bool Invert { get; set; }

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            bool b = value is bool v && v;
            if (value != null && !(value is bool)) b = true; // non-null object counts as true
            return b ^ Invert ? Visibility.Visible : Visibility.Collapsed;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
            value is Visibility v && (v == Visibility.Visible) ^ Invert;
    }

    public sealed class InverseBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => !(value is bool b && b);
    }

    /// <summary>Coverage status → short text with symbol.</summary>
    public sealed class CoverageTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is CoverageStatus s ? Text(s) : "";
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

        public static string Text(CoverageStatus s) => s switch
        {
            CoverageStatus.Covered => "✓ covered",
            CoverageStatus.Partial => "◐ partial",
            CoverageStatus.LowDiodeCount => "! few diodes",
            _ => "✗ not covered",
        };
    }

    /// <summary>Coverage status or validation severity → status brush.</summary>
    public sealed class StatusBrushConverter : IValueConverter
    {
        public static readonly Brush Good = Frozen(Color.FromRgb(0x2E, 0xA0, 0x43));
        public static readonly Brush Caution = Frozen(Color.FromRgb(0xD0, 0x8A, 0x00));
        public static readonly Brush Bad = Frozen(Color.FromRgb(0xD1, 0x34, 0x38));
        public static readonly Brush Neutral = Frozen(Color.FromRgb(0x80, 0x80, 0x80));

        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value switch
        {
            CoverageStatus.Covered => Good,
            CoverageStatus.Partial => Caution,
            CoverageStatus.LowDiodeCount => Caution,
            CoverageStatus.NotCovered => Bad,
            ValidationSeverity.Info => Good,
            ValidationSeverity.Warning => Caution,
            ValidationSeverity.Error => Bad,
            bool b => b ? Good : Bad,
            _ => Neutral,
        };

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
    }

    /// <summary>Distinct colours for PTVs (readable on the dark plane view and in 3D).</summary>
    public static class PtvColours
    {
        private static readonly Color[] Palette =
        {
            Color.FromRgb(0xFF, 0x5C, 0x8A), Color.FromRgb(0x4F, 0xC3, 0xF7), Color.FromRgb(0xFF, 0xB7, 0x4D),
            Color.FromRgb(0x81, 0xC7, 0x84), Color.FromRgb(0xBA, 0x68, 0xC8), Color.FromRgb(0xFF, 0xF1, 0x76),
            Color.FromRgb(0x4D, 0xD0, 0xE1), Color.FromRgb(0xF0, 0x62, 0x92), Color.FromRgb(0xA1, 0x88, 0x7F),
            Color.FromRgb(0x90, 0xA4, 0xAE),
        };

        public static Color For(int index) => Palette[((index % Palette.Length) + Palette.Length) % Palette.Length];
    }
}

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>Non-null (and non-empty string) → true.</summary>
    public sealed class NotNullConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is string s ? s.Length > 0 : value != null;

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new System.NotSupportedException();
    }

    /// <summary>Progress step state → glyph.</summary>
    public sealed class StepGlyphConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value as string switch { "Done" => "✓", "Failed" => "✗", "Pending" => "○", _ => "…" };

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new System.NotSupportedException();
    }
}

namespace SrsMcPsqa.App.Infrastructure
{
    /// <summary>true → Primary button appearance (highlight the action to take), false → Secondary.</summary>
    public sealed class PrimaryIfTrueConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            value is bool b && b ? Wpf.Ui.Controls.ControlAppearance.Primary : Wpf.Ui.Controls.ControlAppearance.Secondary;

        public object ConvertBack(object value, System.Type targetType, object parameter, System.Globalization.CultureInfo culture) =>
            throw new System.NotSupportedException();
    }
}
