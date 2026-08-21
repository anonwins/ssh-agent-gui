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

    public string LoadedCountText =>
        LoadedCount == 1 ? "1 key loaded" : $"{LoadedCount} keys loaded";

    public bool HasKeys => Identities.Count > 0;

    public bool IsEmpty => Identities.Count == 0;

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
            if (await RefreshCoreAsync().ConfigureAwait(true))
                StatusText = RefreshedStatus();
            return true;
        }).ConfigureAwait(true);
    }

    public async Task AddKeyAsync(string path)
    {
        await WithBusy(async () =>
        {
            var before = LoadedFingerprints();
            string? passphrase = null;
            if (PrivateKeyFile.LooksEncrypted(path))
            {
                passphrase = PromptPassphrase(path);
                if (passphrase is null)
                {
                    StatusText = "Load cancelled.";
                    return false;
                }
            }

            var result = await _client.AddAsync(path, passphrase).ConfigureAwait(true);
            var listed = await RefreshCoreAsync().ConfigureAwait(true);
            BindNewLoaded(before, path);
            SetOutcome(result.Ok, result.Message, listed, "Loaded " + Path.GetFileName(path));
            return result.Ok;
        }).ConfigureAwait(true);
    }

    public async Task<bool> UnloadAsync(SshIdentity identity)
    {
        return await WithBusy(async () =>
        {
            var removed = await RemoveLoadedAsync(identity).ConfigureAwait(true);
            if (!removed.Ok)
            {
                StatusText = removed.Message;
                await RefreshCoreAsync().ConfigureAwait(true);
                return false;
            }

            _store.Remove(identity.Fingerprint);
            if (await RefreshCoreAsync().ConfigureAwait(true))
                StatusText = "Unloaded " + identity.DisplayComment;
            return true;
        }).ConfigureAwait(true);
    }

    public async Task<bool> UnloadAllAsync()
    {
        return await WithBusy(async () =>
        {
            var result = await _client.RemoveAllAsync().ConfigureAwait(true);
            var listed = await RefreshCoreAsync().ConfigureAwait(true);
            SetOutcome(result.Ok, result.Message, listed, "Unloaded all keys");
            return result.Ok;
        }).ConfigureAwait(true);
    }

    public async Task<string?> GetPublicKeyAsync(SshIdentity identity)
    {
        return await WithBusy(async () =>
        {
            var line = await FindPublicKeyLineAsync(identity).ConfigureAwait(true);
            if (string.IsNullOrWhiteSpace(line))
            {
                StatusText = "No public key file, and the key is not in the agent.";
                return null;
            }

            StatusText = "Public key copied";
            return line;
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
                var add = await _client.AddAsync(
                        request.Path,
                        string.IsNullOrEmpty(request.Passphrase) ? null : request.Passphrase)
                    .ConfigureAwait(true);
                if (!add.Ok)
                    addFailed = "Key created, but it was not loaded into the agent. " + add.Message;
            }

            var listed = await RefreshCoreAsync().ConfigureAwait(true);
            BindNewLoaded(before, request.Path);
            BindPath(request.Path);
            if (addFailed is not null)
                StatusText = addFailed;
            else if (listed)
                StatusText = "Created " + Path.GetFileName(request.Path);
            if (created.Ok)
                UiSettings.Current.RememberSave(request.Path);
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

    private async Task<bool> RefreshCoreAsync()
    {
        var result = await _client.ListAsync().ConfigureAwait(true);
        var loaded = result.Ok
            ? result.Value ?? []
            : [];

        if (!result.Ok && result.Status is not SshAgentStatus.Empty)
        {
            ApplyRows(loaded);
            StatusText = result.Message;
            return false;
        }

        var loadedSet = loaded.Select(i => i.Fingerprint).ToHashSet(StringComparer.Ordinal);
        _store.KeepOnly(loadedSet);

        foreach (var item in loaded)
        {
            var stored = _store.TryGet(item.Fingerprint);
            item.Path = ResolveExistingPath(item) ?? stored?.Path;
            if (string.IsNullOrWhiteSpace(item.Comment) && stored is not null)
                item.Comment = stored.Comment;
            _store.Remember(item.Fingerprint, item.Path, item.Comment, item.KeyType, item.Bits, persist: false);
        }

        _store.Persist();
        ApplyRows(OrderRows(loaded));
        return true;
    }

    private List<SshIdentity> OrderRows(IReadOnlyList<SshIdentity> loaded)
    {
        var byFingerprint = loaded.ToDictionary(item => item.Fingerprint, StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var identity in Identities)
        {
            if (byFingerprint.ContainsKey(identity.Fingerprint))
                AddUnique(order, identity.Fingerprint);
        }

        foreach (var item in loaded)
            AddUnique(order, item.Fingerprint);

        return order.Select(fingerprint => byFingerprint[fingerprint]).ToList();
    }

    private static void AddUnique(List<string> order, string fingerprint)
    {
        if (!order.Contains(fingerprint, StringComparer.Ordinal))
            order.Add(fingerprint);
    }

    private void ApplyRows(IReadOnlyList<SshIdentity> rows)
    {
        var desired = rows.Select(r => r.Fingerprint).ToHashSet(StringComparer.Ordinal);
        for (var i = Identities.Count - 1; i >= 0; i--)
        {
            if (!desired.Contains(Identities[i].Fingerprint))
                Identities.RemoveAt(i);
        }

        for (var i = 0; i < rows.Count; i++)
        {
            var row = rows[i];
            var existingIndex = IndexOfFingerprint(row.Fingerprint);
            if (existingIndex < 0)
            {
                Identities.Insert(i, row);
                continue;
            }

            MergeRow(Identities[existingIndex], row);
            if (existingIndex != i)
                Identities.Move(existingIndex, i);
        }

        LoadedCount = rows.Count;
        OnPropertyChanged(nameof(HasKeys));
        OnPropertyChanged(nameof(IsEmpty));
    }

    private int IndexOfFingerprint(string fingerprint)
    {
        for (var i = 0; i < Identities.Count; i++)
        {
            if (string.Equals(Identities[i].Fingerprint, fingerprint, StringComparison.Ordinal))
                return i;
        }

        return -1;
    }

    private static void MergeRow(SshIdentity target, SshIdentity source)
    {
        target.Comment = source.Comment;
        target.KeyType = source.KeyType;
        target.Bits = source.Bits;
        if (!string.IsNullOrWhiteSpace(source.Path))
            target.Path = source.Path;
    }

    private async Task<SshAgentResult> RemoveLoadedAsync(SshIdentity identity)
    {
        var path = ResolveExistingPath(identity);
        if (!string.IsNullOrWhiteSpace(path))
            return await _client.RemoveAsync(path).ConfigureAwait(true);

        var line = await FindPublicKeyLineAsync(identity).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(line))
            return SshAgentResult.Fail("Could not unload the key from the agent.");

        return await RemoveViaPublicLineAsync(line).ConfigureAwait(true);
    }

    private async Task<string?> FindPublicKeyLineAsync(SshIdentity identity)
    {
        var path = ResolveExistingPath(identity);
        if (!string.IsNullOrWhiteSpace(path))
        {
            var pub = path + ".pub";
            if (File.Exists(pub))
            {
                var fromFile = ReadFirstLine(pub);
                if (!string.IsNullOrWhiteSpace(fromFile))
                    return fromFile;
            }
        }

        var listed = await _client.ListPublicAsync().ConfigureAwait(true);
        if (!listed.Ok || listed.Value is not { Count: > 0 })
            return null;

        foreach (var line in listed.Value)
        {
            var printed = await _keygen.FingerprintPublicLineAsync(line).ConfigureAwait(true);
            if (printed is { Ok: true, Value: { } found }
                && string.Equals(found.Fingerprint, identity.Fingerprint, StringComparison.Ordinal))
                return line;
        }

        return null;
    }

    private async Task<SshAgentResult> RemoveViaPublicLineAsync(string publicKeyLine)
    {
        var tmp = Path.Combine(Path.GetTempPath(), "ssh-agent-gui-" + Guid.NewGuid().ToString("n") + ".pub");
        try
        {
            await File.WriteAllTextAsync(tmp, publicKeyLine.Trim() + Environment.NewLine).ConfigureAwait(true);
            return await _client.RemoveAsync(tmp).ConfigureAwait(true);
        }
        finally
        {
            try
            {
                File.Delete(tmp);
            }
            catch (IOException)
            {
                // leftover temp is harmless
            }
        }
    }

    private static string? ReadFirstLine(string filePath)
    {
        foreach (var line in File.ReadLines(filePath))
        {
            var trimmed = line.Trim();
            if (trimmed.Length > 0)
                return trimmed;
        }

        return null;
    }

    private void SetOutcome(bool ok, string failMessage, bool listed, string successText)
    {
        if (!ok)
            StatusText = failMessage;
        else if (listed)
            StatusText = successText;
    }

    private string RefreshedStatus() =>
        LoadedCount == 0 ? "Refreshed — no keys loaded"
        : LoadedCount == 1 ? "Refreshed — 1 key loaded"
        : $"Refreshed — {LoadedCount} keys loaded";

    private HashSet<string> LoadedFingerprints() =>
        Identities.Select(i => i.Fingerprint).ToHashSet(StringComparer.Ordinal);

    private void BindNewLoaded(HashSet<string> before, string path)
    {
        foreach (var identity in Identities.Where(i => !before.Contains(i.Fingerprint)))
        {
            identity.Path = path;
            _store.Upsert(identity, path);
        }
    }

    private static string? PromptPassphrase(string keyPath)
    {
        var owner = System.Windows.Application.Current?.MainWindow;
        var dialog = new PassphraseWindow("Enter the passphrase for " + Path.GetFileName(keyPath) + ".");
        if (owner is { IsVisible: true })
        {
            dialog.Owner = owner;
            dialog.WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
        }

        return dialog.ShowDialog() == true ? dialog.Passphrase : null;
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
