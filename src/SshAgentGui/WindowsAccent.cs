using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace SshAgentGui;

internal readonly record struct AccentPalette(
    System.Windows.Media.Color Fill,
    System.Windows.Media.Color Hover,
    System.Windows.Media.Color Press,
    System.Windows.Media.Color Accent,
    System.Windows.Media.Color AccentDark,
    System.Windows.Media.Color Focus,
    System.Windows.Media.Color Text);

internal static class WindowsAccent
{
    internal static readonly System.Windows.Media.Color Bg = System.Windows.Media.Color.FromRgb(0x12, 0x14, 0x1A);
    internal static readonly System.Windows.Media.Color Surface = System.Windows.Media.Color.FromRgb(0x1C, 0x20, 0x28);
    internal static readonly System.Windows.Media.Color DarkText = System.Windows.Media.Color.FromRgb(0x1A, 0x16, 0x08);
    internal static readonly System.Windows.Media.Color LightText = System.Windows.Media.Color.FromRgb(0xF7, 0xED, 0xED);
    private static readonly System.Windows.Media.Color White = System.Windows.Media.Color.FromRgb(255, 255, 255);
    private static readonly System.Windows.Media.Color Black = System.Windows.Media.Color.FromRgb(0, 0, 0);

    public static void Apply(ResourceDictionary resources)
    {
        if (TryRead() is not { } fill)
            return;
        Apply(resources, Derive(fill));
    }

    internal static void Apply(ResourceDictionary resources, AccentPalette palette)
    {
        SetColor(resources, "AccentColor", palette.Accent);
        SetColor(resources, "AccentDarkColor", palette.AccentDark);
        SetColor(resources, "AccentFocusColor", palette.Focus);
        SetBrush(resources, "AccentBrush", palette.Accent);
        SetBrush(resources, "AccentDarkBrush", palette.AccentDark);
        SetBrush(resources, "AccentFocusBrush", palette.Focus);
        SetBrush(resources, "PrimaryFillBrush", palette.Fill);
        SetBrush(resources, "PrimaryHoverBrush", palette.Hover);
        SetBrush(resources, "PrimaryPressBrush", palette.Press);
        SetBrush(resources, "PrimaryTextBrush", palette.Text);
    }

    internal static System.Windows.Media.Color? TryRead()
    {
        if (TryReadDword(@"Software\Microsoft\Windows\DWM", "AccentColor") is { } dwm)
            return FromAbgr(dwm);
        if (TryReadDword(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Accent", "AccentColorMenu") is { } menu)
            return FromAbgr(menu);
        return null;
    }

    internal static System.Windows.Media.Color FromAbgr(uint abgr) =>
        System.Windows.Media.Color.FromArgb(
            255,
            (byte)(abgr & 0xFF),
            (byte)((abgr >> 8) & 0xFF),
            (byte)((abgr >> 16) & 0xFF));

    internal static AccentPalette Derive(System.Windows.Media.Color fill)
    {
        fill.A = 255;
        var luma = Luma(fill);
        var hover = Mix(fill, luma < 0.45 ? White : Black, 0.12);
        var press = luma >= 0.25 ? Mix(fill, Black, 0.22) : Mix(fill, White, 0.18);
        var accent = Lift(fill, Bg, minContrast: 3.0);
        var accentDark = Mix(fill, Surface, 0.78);
        var focus = Lift(Mix(fill, Black, 0.35), Surface, minContrast: 2.5);
        var text = luma >= 0.45 ? DarkText : LightText;
        return new AccentPalette(fill, hover, press, accent, accentDark, focus, text);
    }

    internal static double Contrast(System.Windows.Media.Color a, System.Windows.Media.Color b)
    {
        var l1 = Luma(a);
        var l2 = Luma(b);
        var max = Math.Max(l1, l2);
        var min = Math.Min(l1, l2);
        return (max + 0.05) / (min + 0.05);
    }

    internal static double Luma(System.Windows.Media.Color color) =>
        0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

    private static System.Windows.Media.Color Lift(
        System.Windows.Media.Color color,
        System.Windows.Media.Color against,
        double minContrast)
    {
        var lifted = color;
        for (var i = 0; i < 24 && Contrast(lifted, against) < minContrast && Luma(lifted) < 0.98; i++)
            lifted = Mix(lifted, White, 0.08);
        return lifted;
    }

    private static System.Windows.Media.Color Mix(System.Windows.Media.Color a, System.Windows.Media.Color b, double t)
    {
        t = Math.Clamp(t, 0, 1);
        return System.Windows.Media.Color.FromArgb(
            255,
            MixByte(a.R, b.R, t),
            MixByte(a.G, b.G, t),
            MixByte(a.B, b.B, t));
    }

    private static byte MixByte(byte a, byte b, double t) =>
        (byte)Math.Clamp((int)Math.Round(a * (1 - t) + b * t), 0, 255);

    private static double Channel(byte value)
    {
        var s = value / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }

    private static uint? TryReadDword(string subKey, string name)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(subKey);
            var raw = key?.GetValue(name);
            var dword = raw switch
            {
                int i => unchecked((uint)i),
                uint u => u,
                _ => 0u,
            };
            return dword == 0 ? null : dword;
        }
        catch
        {
            return null;
        }
    }

    private static void SetColor(ResourceDictionary resources, string key, System.Windows.Media.Color color) =>
        resources[key] = color;

    private static void SetBrush(ResourceDictionary resources, string key, System.Windows.Media.Color color) =>
        resources[key] = new SolidColorBrush(color);
}
