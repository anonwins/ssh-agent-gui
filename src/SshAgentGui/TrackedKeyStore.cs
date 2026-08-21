using System.Text.Json;
using System.Text.Json.Serialization;
using SshAgentGui.Ssh;

namespace SshAgentGui;

internal sealed class TrackedKeyRecord
{
    [JsonPropertyName("path")]
    public string Path { get; set; } = "";

    [JsonPropertyName("comment")]
    public string Comment { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("bits")]
    public int Bits { get; set; }
}

internal sealed class TrackedKeyStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    private readonly string _filePath;
    private Dictionary<string, TrackedKeyRecord> _items = new(StringComparer.Ordinal);
    private bool _dirty;

    public TrackedKeyStore(string? filePath = null)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SshAgentGui");
        _filePath = filePath ?? Path.Combine(dir, "keys.json");
    }

    public IReadOnlyDictionary<string, TrackedKeyRecord> Items => _items;

    public void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                _items = new Dictionary<string, TrackedKeyRecord>(StringComparer.Ordinal);
                return;
            }

            var json = File.ReadAllText(_filePath);
            var parsed = JsonSerializer.Deserialize<Dictionary<string, TrackedKeyRecord>>(json, JsonOptions);
            _items = parsed is null
                ? new Dictionary<string, TrackedKeyRecord>(StringComparer.Ordinal)
                : new Dictionary<string, TrackedKeyRecord>(parsed, StringComparer.Ordinal);
        }
        catch
        {
            _items = new Dictionary<string, TrackedKeyRecord>(StringComparer.Ordinal);
        }
    }

    public TrackedKeyRecord? TryGet(string fingerprint) =>
        _items.TryGetValue(fingerprint, out var record) ? record : null;

    public void Upsert(SshIdentity identity, string path) =>
        Remember(identity.Fingerprint, path, identity.Comment, identity.KeyType, identity.Bits);

    public void Upsert(string fingerprint, string path, string comment, string type, int bits) =>
        Remember(fingerprint, path, comment, type, bits);

    public void Remember(string fingerprint, string? path, string comment, string type, int bits, bool persist = true)
    {
        _items.TryGetValue(fingerprint, out var existing);
        var next = new TrackedKeyRecord
        {
            Path = FirstNonEmpty(path, existing?.Path),
            Comment = FirstNonEmpty(comment, existing?.Comment),
            Type = FirstNonEmpty(type, existing?.Type),
            Bits = bits != 0 ? bits : existing?.Bits ?? 0,
        };
        if (existing is not null
            && existing.Path == next.Path
            && existing.Comment == next.Comment
            && existing.Type == next.Type
            && existing.Bits == next.Bits)
            return;

        _items[fingerprint] = next;
        _dirty = true;
        if (persist)
            Save();
    }

    public void Persist()
    {
        if (_dirty)
            Save();
    }

    private static string FirstNonEmpty(string? value, string? fallback) =>
        !string.IsNullOrWhiteSpace(value) ? value : fallback ?? "";

    public void Remove(string fingerprint)
    {
        if (_items.Remove(fingerprint))
            Save();
    }

    public void KeepOnly(IReadOnlySet<string> fingerprints)
    {
        var extra = _items.Keys.Where(key => !fingerprints.Contains(key)).ToList();
        if (extra.Count == 0)
            return;
        foreach (var key in extra)
            _items.Remove(key);
        _dirty = true;
        Save();
    }

    private void Save()
    {
        var dir = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = _filePath + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(_items, JsonOptions));
        File.Copy(tmp, _filePath, overwrite: true);
        File.Delete(tmp);
        _dirty = false;
    }
}
