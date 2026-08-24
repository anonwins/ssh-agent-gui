using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;

namespace SshAgentGui.Ssh;

internal sealed class SshIdentity : INotifyPropertyChanged
{
    private string _comment;
    private string _keyType;
    private int _bits;
    private string? _path;
    private DateTimeOffset? _expiresAt;
    private TimeSpan? _lifetime;

    public SshIdentity(string fingerprint, string comment, string keyType, int bits, string? path = null)
    {
        Fingerprint = fingerprint;
        _comment = comment;
        _keyType = keyType;
        _bits = bits;
        _path = path;
    }

    public string Fingerprint { get; }

    public string Comment
    {
        get => _comment;
        set
        {
            if (SetField(ref _comment, value))
                OnPropertyChanged(nameof(DisplayComment));
        }
    }

    public string KeyType
    {
        get => _keyType;
        set => SetField(ref _keyType, value);
    }

    public int Bits
    {
        get => _bits;
        set => SetField(ref _bits, value);
    }

    public string? Path
    {
        get => _path;
        set
        {
            if (SetField(ref _path, value))
                OnPropertyChanged(nameof(CanReload));
        }
    }

    public DateTimeOffset? ExpiresAt
    {
        get => _expiresAt;
        set
        {
            if (!SetField(ref _expiresAt, value))
                return;
            OnPropertyChanged(nameof(ExpiryText));
            OnPropertyChanged(nameof(HasExpiry));
        }
    }

    public TimeSpan? Lifetime
    {
        get => _lifetime;
        set
        {
            if (SetField(ref _lifetime, value))
                OnPropertyChanged(nameof(CanReload));
        }
    }

    public int LoadGeneration { get; set; }

    public string DisplayComment => string.IsNullOrWhiteSpace(Comment) ? "(no comment)" : Comment;

    public bool HasExpiry => ExpiresAt is not null;

    public bool CanReload =>
        Lifetime is { } duration
        && duration >= TimeSpan.FromSeconds(1)
        && !string.IsNullOrWhiteSpace(Path)
        && File.Exists(Path);

    public string ExpiryText => FormatExpiry(ExpiresAt, DateTimeOffset.UtcNow);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void NotifyExpiryClock() => OnPropertyChanged(nameof(ExpiryText));

    public static string FormatExpiry(DateTimeOffset? expiresAt, DateTimeOffset now)
    {
        if (expiresAt is null)
            return "";

        var remaining = expiresAt.Value - now;
        if (remaining <= TimeSpan.Zero)
            return "Expired";

        var hours = ((int)remaining.TotalHours).ToString(CultureInfo.InvariantCulture);
        var minutes = remaining.Minutes.ToString("D2", CultureInfo.InvariantCulture);
        var seconds = remaining.Seconds.ToString("D2", CultureInfo.InvariantCulture);
        return hours + ":" + minutes + ":" + seconds;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value))
            return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }
}
