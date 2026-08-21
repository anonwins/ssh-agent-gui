using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace SshAgentGui;

internal sealed class UiSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public static UiSettings Current { get; private set; } = new();

    private readonly string _filePath;

    public UiSettings()
    {
        _filePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SshAgentGui",
            "ui.json");
    }

    [JsonPropertyName("normalLeft")]
    public int NormalLeft { get; set; }

    [JsonPropertyName("normalTop")]
    public int NormalTop { get; set; }

    [JsonPropertyName("normalRight")]
    public int NormalRight { get; set; }

    [JsonPropertyName("normalBottom")]
    public int NormalBottom { get; set; }

    [JsonPropertyName("maximized")]
    public bool Maximized { get; set; }

    [JsonPropertyName("openDir")]
    public string? OpenDir { get; set; }

    [JsonPropertyName("saveDir")]
    public string? SaveDir { get; set; }

    public static void Load()
    {
        var settings = new UiSettings();
        try
        {
            if (File.Exists(settings._filePath))
            {
                var parsed = JsonSerializer.Deserialize<UiSettings>(File.ReadAllText(settings._filePath), JsonOptions);
                if (parsed is not null)
                    CopyInto(parsed, settings);
            }
        }
        catch
        {
            // keep defaults
        }

        Current = settings;
    }

    private static void CopyInto(UiSettings from, UiSettings to)
    {
        to.NormalLeft = from.NormalLeft;
        to.NormalTop = from.NormalTop;
        to.NormalRight = from.NormalRight;
        to.NormalBottom = from.NormalBottom;
        to.Maximized = from.Maximized;
        to.OpenDir = from.OpenDir;
        to.SaveDir = from.SaveDir;
    }

    public void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var tmp = _filePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
            File.Copy(tmp, _filePath, overwrite: true);
            File.Delete(tmp);
        }
        catch
        {
            // settings are non-critical
        }
    }

    public void Capture(Window window)
    {
        if (!NativeWindowPlacement.TryCapture(window, out var bounds, out var maximized))
            return;
        NormalLeft = bounds.Left;
        NormalTop = bounds.Top;
        NormalRight = bounds.Right;
        NormalBottom = bounds.Bottom;
        Maximized = maximized;
    }

    public void Apply(Window window)
    {
        var bounds = new NativeWindowPlacement.RectPixels(NormalLeft, NormalTop, NormalRight, NormalBottom);
        NativeWindowPlacement.TryApply(window, bounds, Maximized);
    }

    public string? ExistingOpenDir() => ExistingDir(OpenDir);

    public string? ExistingSaveDir() => ExistingDir(SaveDir);

    public void RememberOpen(string filePath) => Remember(filePath, open: true);

    public void RememberSave(string filePath) => Remember(filePath, open: false);

    private void Remember(string filePath, bool open)
    {
        var dir = Path.GetDirectoryName(filePath);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
            return;
        if (open)
            OpenDir = dir;
        else
            SaveDir = dir;
        Save();
    }

    private static string? ExistingDir(string? dir) =>
        !string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir) ? dir : null;
}
