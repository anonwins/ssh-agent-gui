using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Threading;
using SshAgentGui.Ssh;

namespace SshAgentGui;

internal readonly record struct ExpirySnapshot(string Fingerprint, int LoadGeneration, DateTimeOffset ExpiresAt);

internal sealed class AgentSession : INotifyPropertyChanged, IDisposable
{
    private readonly ISshAgentClient _client;
    private readonly WindowsSshKeygen _keygen;
    private readonly TrackedKeyStore _store;
    private readonly WindowsSshAgentService _service;
    private readonly TimeProvider _time;
    private readonly HashSet<(string Fingerprint, int Generation)> _expireFail = [];
    private DispatcherTimer? _timer;
    private bool _isBusy;
    private string _statusText = "Starting…";
    private int _loadedCount;
    private bool _isAgentUnavailable;
    private bool _isBinaryMissing;
    private bool _canStartAgent;
    private SshAgentServiceState _serviceState = SshAgentServiceState.Stopped;
    private string _agentDownDetail = "";
    private bool _disposed;

    public AgentSession(
        ISshAgentClient? client = null,
        WindowsSshKeygen? keygen = null,
        TrackedKeyStore? store = null,
        WindowsSshAgentService? service = null,
        TimeProvider? time = null)
    {
        _client = client ?? new WindowsOpenSshClient();
        _keygen = keygen ?? new WindowsSshKeygen();
        _store = store ?? new TrackedKeyStore();
        _service = service ?? new WindowsSshAgentService();
        _time = time ?? TimeProvider.System;
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
            OnPropertyChanged(nameof(CanUseAgent));
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
        IsAgentUnavailable ? "Agent not running"
        : LoadedCount == 1 ? "1 key loaded"
        : $"{LoadedCount} keys loaded";

    public bool HasKeys => Identities.Count > 0;

    public bool IsEmpty => Identities.Count == 0;

    public bool IsAgentUnavailable => _isAgentUnavailable;

    public bool IsBinaryMissing => _isBinaryMissing;

    public bool ShowNoKeysHint => IsEmpty && !IsAgentUnavailable && !IsBinaryMissing;

    public bool CanUseAgent => IsIdle && !IsAgentUnavailable && !IsBinaryMissing;

    public bool CanStartAgent => _canStartAgent;

    public string AgentDownDetail => _agentDownDetail;

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

    public async Task AddKeyAsync(string path, TimeSpan? lifetime = null)
    {
        await WithBusy(async () =>
        {
            path = Path.GetFullPath(path);
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

            var result = await _client.AddAsync(path, passphrase, lifetime).ConfigureAwait(true);
            if (result.Ok)
                await PersistExpiryForFileAsync(path, lifetime).ConfigureAwait(true);

            var listed = await RefreshCoreAsync().ConfigureAwait(true);
            if (result.Ok)
                await StampLoadedAsync(path, lifetime).ConfigureAwait(true);
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
            DropRow(identity.Fingerprint);
            if (await RefreshCoreAsync().ConfigureAwait(true))
                StatusText = "Unloaded " + UnloadLabel(identity);
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
            var path = Path.GetFullPath(request.Path);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var created = await _keygen.CreateAsync(
                    request.Type,
                    path,
                    request.Comment,
                    request.Passphrase)
                .ConfigureAwait(true);
            if (!created.Ok)
            {
                StatusText = created.Message;
                return false;
            }

            var printed = await _keygen.FingerprintAsync(path).ConfigureAwait(true);
            if (printed is { Ok: true, Value: { } identity })
            {
                identity.Path = path;
                _store.Upsert(identity, path);
            }

            var before = LoadedFingerprints();
            string? addFailed = null;
            var added = false;
            if (request.LoadIntoAgent)
            {
                var add = await _client.AddAsync(
                        path,
                        string.IsNullOrEmpty(request.Passphrase) ? null : request.Passphrase,
                        request.Lifetime)
                    .ConfigureAwait(true);
                if (add.Ok)
                {
                    added = true;
                    await PersistExpiryForFileAsync(path, request.Lifetime).ConfigureAwait(true);
                }
                else
                    addFailed = "Key created, but it was not loaded into the agent. " + add.Message;
            }

            var listed = await RefreshCoreAsync().ConfigureAwait(true);
            if (added)
                await StampLoadedAsync(path, request.Lifetime).ConfigureAwait(true);
            BindNewLoaded(before, path);
            BindPath(path);
            if (addFailed is not null)
                StatusText = addFailed;
            else if (listed)
                StatusText = "Created " + Path.GetFileName(path);
            if (created.Ok)
                UiSettings.Current.RememberSave(path);
            return created.Ok;
        }).ConfigureAwait(true);
    }

    public async Task StartAgentAsync()
    {
        await WithBusy(async () =>
        {
            var state = await Task.Run(_service.Query).ConfigureAwait(true);
            if (state is SshAgentServiceState.Missing or SshAgentServiceState.Disabled)
            {
                await RefreshCoreAsync().ConfigureAwait(true);
                return false;
            }

            var didStart = false;
            if (state != SshAgentServiceState.Running)
            {
                StatusText = "Starting the OpenSSH Authentication Agent…";
                var started = await Task.Run(_service.TryStart).ConfigureAwait(true);
                if (started.Kind == SshAgentServiceStartKind.NeedsElevation)
                    started = await _service.TryStartElevatedAsync().ConfigureAwait(true);

                if (started.Kind == SshAgentServiceStartKind.Cancelled)
                {
                    StatusText = started.Message;
                    return false;
                }

                if (!started.Succeeded)
                {
                    await RefreshCoreAsync().ConfigureAwait(true);
                    StatusText = started.Message;
                    return false;
                }

                didStart = true;
            }

            var listed = await RefreshCoreAsync().ConfigureAwait(true);
            if (listed)
                StatusText = didStart ? "Agent started." : RefreshedStatus();
            return listed;
        }).ConfigureAwait(true);
    }

    internal async Task<bool> TryExpireAsync(ExpirySnapshot captured)
    {
        return await WithBusy(async () =>
        {
            var current = FindIdentity(captured.Fingerprint);
            if (current is null
                || current.LoadGeneration != captured.LoadGeneration
                || current.ExpiresAt is not { } exp
                || exp > _time.GetUtcNow())
                return false;

            var removed = await RemoveLoadedAsync(current).ConfigureAwait(true);
            if (!removed.Ok)
            {
                _expireFail.Add((captured.Fingerprint, captured.LoadGeneration));
                StatusText = removed.Message;
                return false;
            }

            _expireFail.Remove((captured.Fingerprint, captured.LoadGeneration));
            _store.Remove(current.Fingerprint);
            DropRow(current.Fingerprint);
            await RefreshCoreAsync().ConfigureAwait(true);
            StatusText = "Unloaded " + UnloadLabel(current);
            return true;
        }).ConfigureAwait(true);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        StopTimer();
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
        if (!result.Ok && result.Status is not SshAgentStatus.Empty)
        {
            SetAvailability(result.Status);
            StatusText = result.Status is SshAgentStatus.AgentUnavailable
                ? AgentDownStatus()
                : result.Message;
            return false;
        }

        SetAvailability(result.Status);
        ApplyListed(result.Ok ? result.Value ?? [] : []);
        await UnloadOverdueAsync().ConfigureAwait(true);
        return true;
    }

    private void ApplyListed(IReadOnlyList<SshIdentity> loaded)
    {
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
        ApplyStoredExpiry();
        EnsureTimer();
    }

    private void ApplyStoredExpiry()
    {
        foreach (var identity in Identities)
        {
            var stored = _store.TryGet(identity.Fingerprint);
            identity.ExpiresAt = stored?.ExpiresAtUtc;
        }
    }

    private async Task UnloadOverdueAsync()
    {
        var now = _time.GetUtcNow();
        var due = Identities
            .Where(i => i.ExpiresAt is { } exp && exp <= now)
            .Select(i => new ExpirySnapshot(i.Fingerprint, i.LoadGeneration, i.ExpiresAt!.Value))
            .ToList();

        foreach (var captured in due)
        {
            var current = FindIdentity(captured.Fingerprint);
            if (current is null
                || current.LoadGeneration != captured.LoadGeneration
                || current.ExpiresAt is not { } exp
                || exp > now)
                continue;

            var removed = await RemoveLoadedAsync(current).ConfigureAwait(true);
            if (!removed.Ok)
            {
                _expireFail.Add((captured.Fingerprint, captured.LoadGeneration));
                StatusText = removed.Message;
                continue;
            }

            _expireFail.Remove((captured.Fingerprint, captured.LoadGeneration));
            _store.Remove(current.Fingerprint);
            DropRow(current.Fingerprint);
        }
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

        LoadedCount = Identities.Count;
        OnPropertyChanged(nameof(HasKeys));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowNoKeysHint));
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

    private SshIdentity? FindIdentity(string fingerprint)
    {
        var index = IndexOfFingerprint(fingerprint);
        return index < 0 ? null : Identities[index];
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
        if (!string.IsNullOrWhiteSpace(path)
            && await FingerprintMatchesAsync(path, identity.Fingerprint).ConfigureAwait(true))
            return await _client.RemoveAsync(path).ConfigureAwait(true);

        var line = await FindPublicKeyLineAsync(identity).ConfigureAwait(true);
        if (string.IsNullOrWhiteSpace(line))
            return SshAgentResult.Fail("Could not unload the key from the agent.");

        return await RemoveViaPublicLineAsync(line).ConfigureAwait(true);
    }

    private async Task<bool> FingerprintMatchesAsync(string path, string fingerprint)
    {
        var printed = await _keygen.FingerprintAsync(path).ConfigureAwait(true);
        return printed is { Ok: true, Value: { } found }
               && string.Equals(found.Fingerprint, fingerprint, StringComparison.Ordinal);
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
                if (!string.IsNullOrWhiteSpace(fromFile)
                    && await FingerprintMatchesAsync(pub, identity.Fingerprint).ConfigureAwait(true))
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

    private async Task PersistExpiryForFileAsync(string path, TimeSpan? lifetime)
    {
        var printed = await _keygen.FingerprintAsync(path).ConfigureAwait(true);
        if (printed is not { Ok: true, Value: { } identity })
            return;

        _store.Upsert(identity, path);
        _store.SetExpiry(identity.Fingerprint, ExpiresAtFrom(lifetime));
    }

    private async Task StampLoadedAsync(string path, TimeSpan? lifetime)
    {
        var printed = await _keygen.FingerprintAsync(path).ConfigureAwait(true);
        if (printed is not { Ok: true, Value: { } printedIdentity })
            return;

        var identity = FindIdentity(printedIdentity.Fingerprint);
        if (identity is null)
            return;

        identity.Path = path;
        identity.ExpiresAt = ExpiresAtFrom(lifetime);
        identity.LoadGeneration++;
        _expireFail.Remove((identity.Fingerprint, identity.LoadGeneration - 1));
        _store.Upsert(identity, path);
        _store.SetExpiry(identity.Fingerprint, identity.ExpiresAt);
        EnsureTimer();
    }

    private DateTimeOffset? ExpiresAtFrom(TimeSpan? lifetime) =>
        lifetime is { } duration && duration >= TimeSpan.FromSeconds(1)
            ? _time.GetUtcNow().ToOffset(TimeSpan.Zero) + duration
            : null;

    private void DropRow(string fingerprint)
    {
        var index = IndexOfFingerprint(fingerprint);
        if (index < 0)
            return;
        Identities.RemoveAt(index);
        LoadedCount = Identities.Count;
        OnPropertyChanged(nameof(HasKeys));
        OnPropertyChanged(nameof(IsEmpty));
        OnPropertyChanged(nameof(ShowNoKeysHint));
    }

    private void EnsureTimer()
    {
        if (_timer is not null || _disposed)
            return;
        if (!Identities.Any(i => i.ExpiresAt is not null))
            return;

        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is null)
            return;

        void Create()
        {
            if (_timer is not null || _disposed)
                return;
            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
            _timer.Tick += OnExpiryTick;
            _timer.Start();
        }

        if (dispatcher.CheckAccess())
            Create();
        else
            dispatcher.BeginInvoke(Create);
    }

    private void StopTimer()
    {
        if (_timer is null)
            return;
        _timer.Stop();
        _timer.Tick -= OnExpiryTick;
        _timer = null;
    }

    private void OnExpiryTick(object? sender, EventArgs e)
    {
        foreach (var identity in Identities)
        {
            if (identity.ExpiresAt is not null)
                identity.NotifyExpiryClock();
        }

        var now = _time.GetUtcNow();
        foreach (var identity in Identities)
        {
            if (identity.ExpiresAt is not { } exp || exp > now)
                continue;
            if (_expireFail.Contains((identity.Fingerprint, identity.LoadGeneration)))
                continue;

            var snapshot = new ExpirySnapshot(identity.Fingerprint, identity.LoadGeneration, exp);
            _ = TryExpireAsync(snapshot);
            break;
        }
    }

    private void SetOutcome(bool ok, string failMessage, bool listed, string successText)
    {
        if (!ok)
            StatusText = failMessage;
        else if (listed)
            StatusText = successText;
    }

    private void SetAvailability(SshAgentStatus status)
    {
        var unavailable = status == SshAgentStatus.AgentUnavailable;
        var missing = status == SshAgentStatus.BinaryMissing;
        var serviceState = SshAgentServiceState.Stopped;
        var canStart = false;
        var detail = "";

        if (unavailable)
        {
            serviceState = _service.Query();
            canStart = serviceState is SshAgentServiceState.Stopped or SshAgentServiceState.Running;
            detail = serviceState switch
            {
                SshAgentServiceState.Disabled =>
                    "The service is disabled. Set startup to Manual or Automatic in Services, then start it.",
                SshAgentServiceState.Missing =>
                    "The OpenSSH Authentication Agent service was not found.",
                _ => "Start the Windows ssh-agent service to load and copy keys.",
            };
        }

        var changed = _isAgentUnavailable != unavailable
            || _isBinaryMissing != missing
            || _canStartAgent != canStart
            || _agentDownDetail != detail
            || _serviceState != serviceState;

        _isAgentUnavailable = unavailable;
        _isBinaryMissing = missing;
        _canStartAgent = canStart;
        _agentDownDetail = detail;
        _serviceState = serviceState;

        if (!changed)
            return;

        OnPropertyChanged(nameof(IsAgentUnavailable));
        OnPropertyChanged(nameof(IsBinaryMissing));
        OnPropertyChanged(nameof(ShowNoKeysHint));
        OnPropertyChanged(nameof(CanUseAgent));
        OnPropertyChanged(nameof(CanStartAgent));
        OnPropertyChanged(nameof(AgentDownDetail));
        OnPropertyChanged(nameof(LoadedCountText));
    }

    private string AgentDownStatus() =>
        _serviceState switch
        {
            SshAgentServiceState.Disabled =>
                "The OpenSSH Authentication Agent service is disabled. Set it to Manual or Automatic in Services, then start it.",
            SshAgentServiceState.Missing =>
                "The OpenSSH Authentication Agent service was not found.",
            _ => "The OpenSSH Authentication Agent is not running.",
        };

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

    private static string UnloadLabel(SshIdentity identity)
    {
        if (!string.IsNullOrWhiteSpace(identity.Path))
            return Path.GetFileName(identity.Path);
        if (!string.IsNullOrWhiteSpace(identity.Comment))
        {
            var name = Path.GetFileName(identity.Comment);
            if (!string.IsNullOrWhiteSpace(name) && name != identity.Comment)
                return name;
        }

        return "the key";
    }

    private static string? PromptPassphrase(string keyPath)
    {
        var owner = Application.Current?.MainWindow;
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
    bool LoadIntoAgent,
    TimeSpan? Lifetime);
