using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using SshAgentGui.Ssh;

namespace SshAgentGui;

internal sealed class AgentSession : INotifyPropertyChanged
{
    private readonly ISshAgentClient _client;
    private readonly WindowsSshKeygen _keygen;
    private readonly TrackedKeyStore _store;
    private bool _isBusy;
    private string _statusText = "Starting…";
    private int _loadedCount;
    private int _disabledCount;

    public AgentSession(ISshAgentClient? client = null, WindowsSshKeygen? keygen = null, TrackedKeyStore? store = null)
    {
        _client = client ?? new WindowsOpenSshClient();
        _keygen = keygen ?? new WindowsSshKeygen();
        _store = store ?? new TrackedKeyStore();
        _store.Load();
    }

    public ObservableCollection<SshIdentity> Identities { get; } = [];

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (_isBusy == value)
                return;
            _isBusy = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsIdle));
        }
    }

    public bool IsIdle => !IsBusy;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (_statusText == value)
                return;
            _statusText = value;
            OnPropertyChanged();
        }
    }

    public int LoadedCount
    {
        get => _loadedCount;
        private set
        {
            if (_loadedCount == value)
                return;
            _loadedCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(LoadedCountText));
        }
    }

    public int DisabledCount
    {
        get => _disabledCount;
        private set
        {
            if (_disabledCount == value)
                return;
            _disabledCount = value;
            OnPropertyChanged();
        }
    }

    public string LoadedCountText =>
        LoadedCount == 1 ? "1 key loaded" : $"{LoadedCount} keys loaded";

    public event PropertyChangedEventHandler? PropertyChanged;

    public string? ResolveExistingPath(SshIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.Path) && File.Exists(identity.Path))
            return identity.Path;

        var stored = _store.TryGet(identity.Fingerprint);
        if (stored is not null && File.Exists(stored.Path))
            return stored.Path;

        if (!string.IsNullOrWhiteSpace(identity.Comment) && File.Exists(identity.Comment))
            return identity.Comment;

        return null;
    }

    public async Task RefreshAsync()
    {
        await WithBusy(async () =>
        {
            await RefreshCoreAsync().ConfigureAwait(true);
            return true;
        }).ConfigureAwait(true);
    }

    public async Task AddKeyAsync(string path)
    {
        await WithBusy(async () =>
        {
            var before = LoadedFingerprints();
            var result = await _client.AddAsync(path, interactive: true).ConfigureAwait(true);
            await RefreshCoreAsync().ConfigureAwait(true);
            BindNewLoaded(before, path);
            if (!result.Ok)
                StatusText = result.Message;
            return result.Ok;
        }).ConfigureAwait(true);
    }

    public async Task<bool> EnableAsync(SshIdentity identity, string path)
    {
        return await WithBusy(async () =>
        {
            var before = LoadedFingerprints();
            var result = await _client.AddAsync(path, interactive: true).ConfigureAwait(true);
            if (result.Ok)
                _store.Upsert(identity.Fingerprint, path, identity.Comment, identity.KeyType, identity.Bits);
            await RefreshCoreAsync().ConfigureAwait(true);
            BindNewLoaded(before, path);
            if (!result.Ok)
                StatusText = result.Message;
            return result.Ok;
        }).ConfigureAwait(true);
    }

    public async Task<bool> DisableAsync(SshIdentity identity, string path)
    {
        return await WithBusy(async () =>
        {
            identity.Path = path;
            _store.Upsert(identity, path);
            var result = await _client.RemoveAsync(path).ConfigureAwait(true);
            await RefreshCoreAsync().ConfigureAwait(true);
            if (!result.Ok)
                StatusText = result.Message;
            return result.Ok;
        }).ConfigureAwait(true);
    }

    public async Task<bool> UnloadAsync(SshIdentity identity, string? path)
    {
        return await WithBusy(async () =>
        {
            if (identity.IsLoaded)
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    StatusText = "Select the key file to unload.";
                    return false;
                }

                var result = await _client.RemoveAsync(path).ConfigureAwait(true);
                if (!result.Ok)
                {
                    StatusText = result.Message;
                    await RefreshCoreAsync().ConfigureAwait(true);
                    return false;
                }
            }

            _store.Remove(identity.Fingerprint);
            await RefreshCoreAsync().ConfigureAwait(true);
            return true;
        }).ConfigureAwait(true);
    }

    public async Task<bool> UnloadAllAsync()
    {
        return await WithBusy(async () =>
        {
            var result = await _client.RemoveAllAsync().ConfigureAwait(true);
            await RefreshCoreAsync().ConfigureAwait(true);
            if (!result.Ok)
                StatusText = result.Message;
            return result.Ok;
        }).ConfigureAwait(true);
    }

    public async Task<bool> CreateKeyAsync(CreateKeyRequest request)
    {
        return await WithBusy(async () =>
        {
            var dir = Path.GetDirectoryName(request.Path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var created = await _keygen.CreateAsync(
                    request.Type,
                    request.Path,
                    request.Comment,
                    request.Passphrase)
                .ConfigureAwait(true);
            if (!created.Ok)
            {
                StatusText = created.Message;
                return false;
            }

            var printed = await _keygen.FingerprintAsync(request.Path).ConfigureAwait(true);
            if (printed is { Ok: true, Value: { } identity })
            {
                identity.Path = request.Path;
                _store.Upsert(identity, request.Path);
            }

            var before = LoadedFingerprints();
            string? addFailed = null;
            if (request.LoadIntoAgent)
            {
                var interactive = !string.IsNullOrEmpty(request.Passphrase);
                var add = await _client.AddAsync(request.Path, interactive).ConfigureAwait(true);
                if (!add.Ok && !interactive)
                    add = await _client.AddAsync(request.Path, interactive: true).ConfigureAwait(true);
                if (!add.Ok)
                    addFailed = "Key created, but it was not loaded into the agent. " + add.Message;
            }

            await RefreshCoreAsync().ConfigureAwait(true);
            BindNewLoaded(before, request.Path);
            BindPath(request.Path);
            if (addFailed is not null)
                StatusText = addFailed;
            return created.Ok;
        }).ConfigureAwait(true);
    }

    private void BindPath(string path)
    {
        foreach (var identity in Identities)
        {
            if (string.IsNullOrEmpty(identity.Path) || !File.Exists(identity.Path))
            {
                var stored = _store.TryGet(identity.Fingerprint);
                if (stored is not null && PathsEqual(stored.Path, path))
                    identity.Path = path;
            }
        }
    }

    private async Task RefreshCoreAsync()
    {
        var result = await _client.ListAsync().ConfigureAwait(true);
        var loaded = result.Ok
            ? result.Value ?? []
            : [];

        if (!result.Ok && result.Status is not SshAgentStatus.Empty)
        {
            ApplyRows(loaded);
            StatusText = result.Message;
            return;
        }

        var loadedSet = loaded.Select(i => i.Fingerprint).ToHashSet(StringComparer.Ordinal);
        _store.DropMissingFilesNotInAgent(loadedSet);

        var rows = new List<SshIdentity>();
        foreach (var item in loaded)
        {
            var stored = _store.TryGet(item.Fingerprint);
            if (stored is not null)
            {
                item.Path = File.Exists(stored.Path) ? stored.Path : item.Path;
                if (string.IsNullOrWhiteSpace(item.Comment))
                    item.Comment = stored.Comment;
            }

            item.IsLoaded = true;
            rows.Add(item);
        }

        foreach (var (fingerprint, stored) in _store.Items)
        {
            if (loadedSet.Contains(fingerprint))
                continue;
            rows.Add(new SshIdentity(
                fingerprint,
                stored.Comment,
                stored.Type,
                stored.Bits,
                File.Exists(stored.Path) ? stored.Path : stored.Path,
                isLoaded: false));
        }

        ApplyRows(rows);
        UpdateStatusFromCounts();
    }

    private void ApplyRows(IReadOnlyList<SshIdentity> rows)
    {
        Identities.Clear();
        foreach (var row in rows)
            Identities.Add(row);

        LoadedCount = rows.Count(r => r.IsLoaded);
        DisabledCount = rows.Count(r => !r.IsLoaded);
    }

    private void UpdateStatusFromCounts()
    {
        if (LoadedCount == 0 && DisabledCount == 0)
            StatusText = "No keys loaded";
        else if (DisabledCount == 0)
            StatusText = LoadedCountText;
        else
            StatusText = $"{LoadedCount} loaded, {DisabledCount} disabled";
    }

    private HashSet<string> LoadedFingerprints() =>
        Identities.Where(i => i.IsLoaded).Select(i => i.Fingerprint).ToHashSet(StringComparer.Ordinal);

    private void BindNewLoaded(HashSet<string> before, string path)
    {
        foreach (var identity in Identities.Where(i => i.IsLoaded && !before.Contains(i.Fingerprint)))
        {
            identity.Path = path;
            _store.Upsert(identity, path);
        }
    }

    private static bool PathsEqual(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private async Task<T> WithBusy<T>(Func<Task<T>> action)
    {
        if (IsBusy)
            return default!;
        IsBusy = true;
        try
        {
            return await action().ConfigureAwait(true);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

internal sealed record CreateKeyRequest(
    string Type,
    string Path,
    string Comment,
    string Passphrase,
    bool LoadIntoAgent);
