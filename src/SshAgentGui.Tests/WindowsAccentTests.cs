using System.Windows;
using System.Windows.Media;

namespace SshAgentGui.Tests;

public sealed class WindowsAccentTests
{
    [Fact]
    public void FromAbgr_decodes_gold_and_forces_opaque()
    {
        var color = WindowsAccent.FromAbgr(0xFF58C4E0);
        Assert.Equal(255, color.A);
        Assert.Equal(0xE0, color.R);
        Assert.Equal(0xC4, color.G);
        Assert.Equal(0x58, color.B);
    }

    [Fact]
    public void Derive_picks_light_text_on_dark_fill()
    {
        var palette = WindowsAccent.Derive(Color.FromRgb(0x0A, 0x16, 0x28));
        Assert.Equal(WindowsAccent.LightText, palette.Text);
        Assert.Equal(Color.FromRgb(0x0A, 0x16, 0x28), palette.Fill);
    }

    [Fact]
    public void Derive_picks_dark_text_on_light_fill()
    {
        var palette = WindowsAccent.Derive(Color.FromRgb(0xF2, 0xE6, 0xA0));
        Assert.Equal(WindowsAccent.DarkText, palette.Text);
    }

    [Fact]
    public void Derive_lifts_near_black_accent_off_background()
    {
        var palette = WindowsAccent.Derive(Color.FromRgb(0x05, 0x05, 0x05));
        Assert.True(WindowsAccent.Contrast(palette.Accent, WindowsAccent.Bg) >= 3.0);
    }

    [Fact]
    public void Apply_replaces_frozen_and_missing_brushes()
    {
        var frozen = new SolidColorBrush(Colors.Gold);
        frozen.Freeze();
        var resources = new ResourceDictionary { ["AccentBrush"] = frozen };
        var palette = WindowsAccent.Derive(Color.FromRgb(0x00, 0x78, 0xD4));

        WindowsAccent.Apply(resources, palette);

        Assert.NotSame(frozen, resources["AccentBrush"]);
        Assert.Equal(Colors.Gold, frozen.Color);
        Assert.Equal(palette.Accent, ((SolidColorBrush)resources["AccentBrush"]).Color);
        Assert.Equal(palette.Fill, ((SolidColorBrush)resources["PrimaryFillBrush"]).Color);
    }
}
