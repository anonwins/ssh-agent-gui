using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace SshAgentGui.Ssh;

internal sealed class SshIdentity : INotifyPropertyChanged
{
    private string _comment;
    private string _keyType;
    private int _bits;
    private string? _path;
    private bool _isLoaded;

    public SshIdentity(string fingerprint, string comment, string keyType, int bits, string? path = null, bool isLoaded = false)
    {
        Fingerprint = fingerprint;
        _comment = comment;
        _keyType = keyType;
        _bits = bits;
        _path = path;
        _isLoaded = isLoaded;
    }

    public string Fingerprint { get; }

    public string Comment
    {
        get => _comment;
        set => SetField(ref _comment, value);
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

    public bool IsLoaded
    {
        get => _isLoaded;
        set
        {
            if (SetField(ref _isLoaded, value))
                OnPropertyChanged(nameof(ToggleLabel));
        }
    }

    public string ToggleLabel => IsLoaded ? "Disable" : "Enable";

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
