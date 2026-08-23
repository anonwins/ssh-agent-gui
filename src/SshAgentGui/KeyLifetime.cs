namespace SshAgentGui;

internal sealed record KeyLifetime(string Label, TimeSpan? Duration)
{
    public static IReadOnlyList<KeyLifetime> Presets { get; } =
    [
        new("Until unloaded", null),
        new("30 minutes", TimeSpan.FromMinutes(30)),
        new("1 hour", TimeSpan.FromHours(1)),
        new("8 hours", TimeSpan.FromHours(8)),
    ];

    public override string ToString() => Label;
}
