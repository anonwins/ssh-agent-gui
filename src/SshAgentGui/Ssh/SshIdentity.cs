using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SshAgentGui.Ssh;

internal sealed class SshIdentity : INotifyPropertyChanged
{
    private string _comment;
    private string _keyType;
    private int _bits;
    private string? _path;

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

    public string DisplayComment => string.IsNullOrWhiteSpace(Comment) ? "(no comment)" : Comment;

    public event PropertyChangedEventHandler? PropertyChanged;

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
