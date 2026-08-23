using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SshAgentGui.Ssh;

internal sealed class SshIdentity : INotifyPropertyChanged
{
    private string _comment;
    private string _keyType;
    private int _bits;
    private string? _path;
    private DateTimeOffset? _expiresAt;

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
        set => SetField(ref _path, value);
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

    public int LoadGeneration { get; set; }

    public string DisplayComment => string.IsNullOrWhiteSpace(Comment) ? "(no comment)" : Comment;

    public bool HasExpiry => ExpiresAt is not null;

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
        if (remaining.TotalHours >= 1)
            return $"{(int)remaining.TotalHours}h left";
        if (remaining.TotalMinutes >= 1)
            return $"{(int)remaining.TotalMinutes}m left";
        return "<1m left";
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
