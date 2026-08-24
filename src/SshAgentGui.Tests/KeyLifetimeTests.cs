namespace SshAgentGui.Tests;

public sealed class KeyLifetimeTests
{
    [Fact]
    public void FromDuration_matches_preset_or_until_unloaded()
    {
        Assert.Same(KeyLifetime.Presets[0], KeyLifetime.FromDuration(null));
        Assert.Same(KeyLifetime.Presets[1], KeyLifetime.FromDuration(TimeSpan.FromMinutes(30)));
        Assert.Same(KeyLifetime.Presets[2], KeyLifetime.FromDuration(TimeSpan.FromHours(1)));
        Assert.Same(KeyLifetime.Presets[3], KeyLifetime.FromDuration(TimeSpan.FromHours(8)));
        Assert.Same(KeyLifetime.Presets[0], KeyLifetime.FromDuration(TimeSpan.FromMinutes(99)));
    }

    [Fact]
    public void FromSeconds_matches_preset_or_until_unloaded()
    {
        Assert.Same(KeyLifetime.Presets[0], KeyLifetime.FromSeconds(null));
        Assert.Same(KeyLifetime.Presets[0], KeyLifetime.FromSeconds(0));
        Assert.Same(KeyLifetime.Presets[1], KeyLifetime.FromSeconds(1800));
        Assert.Same(KeyLifetime.Presets[2], KeyLifetime.FromSeconds(3600));
    }
}
