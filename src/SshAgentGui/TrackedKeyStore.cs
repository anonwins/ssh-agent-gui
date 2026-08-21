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

    public void Upsert(SshIdentity identity, string path)
    {
        _items[identity.Fingerprint] = new TrackedKeyRecord
        {
            Path = path,
            Comment = identity.Comment,
            Type = identity.KeyType,
            Bits = identity.Bits,
        };
        Save();
    }

    public void Upsert(string fingerprint, string path, string comment, string type, int bits)
    {
        _items[fingerprint] = new TrackedKeyRecord
        {
            Path = path,
            Comment = comment,
            Type = type,
            Bits = bits,
        };
        Save();
    }

    public void Remove(string fingerprint)
    {
        if (_items.Remove(fingerprint))
            Save();
    }

    public void DropMissingFilesNotInAgent(IReadOnlySet<string> loadedFingerprints)
    {
        var stale = _items
            .Where(pair => !loadedFingerprints.Contains(pair.Key) && !File.Exists(pair.Value.Path))
            .Select(pair => pair.Key)
            .ToList();
        if (stale.Count == 0)
            return;
        foreach (var key in stale)
            _items.Remove(key);
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
    }
}
