namespace SshAgentGui;

internal sealed record KeyLifetime(string Label, TimeSpan? Duration)
{
    public static IReadOnlyList<KeyLifetime> Presets { get; } =
    [
        new("Until unloaded", null),
        new("30 minutes — auto-unload while this app is running", TimeSpan.FromMinutes(30)),
        new("1 hour — auto-unload while this app is running", TimeSpan.FromHours(1)),
        new("8 hours — auto-unload while this app is running", TimeSpan.FromHours(8)),
    ];
}
