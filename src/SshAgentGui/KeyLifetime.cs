namespace SshAgentGui;

internal sealed record KeyLifetime(string Label, TimeSpan? Duration)
{
    public static IReadOnlyList<KeyLifetime> Presets { get; } =
    [
        new("Off", null),
        new("30 minutes", TimeSpan.FromMinutes(30)),
        new("1 hour", TimeSpan.FromHours(1)),
        new("8 hours", TimeSpan.FromHours(8)),
    ];

    public override string ToString() => Label;

    public static KeyLifetime FromDuration(TimeSpan? duration)
    {
        foreach (var preset in Presets)
        {
            if (preset.Duration == duration)
                return preset;
        }

        return Presets[0];
    }

    public static KeyLifetime FromSeconds(int? seconds) =>
        FromDuration(seconds is { } value && value >= 1 ? TimeSpan.FromSeconds(value) : null);
}
