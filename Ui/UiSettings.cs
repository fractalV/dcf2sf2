using System.Text.Json;

namespace DcfToSf2.Ui;

internal sealed class UiSettings
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public string? LastInputFile { get; set; }
    public string? LastOutputDir { get; set; }
    public bool TwoColumn { get; set; }
    public bool ForceList { get; set; }

    /// <summary>Last selected ignore fields (also used when no per-file entry).</summary>
    public List<string> IgnoreFields { get; set; } = [];

    /// <summary>Ignore fields keyed by normalized full path of the DCF file.</summary>
    public Dictionary<string, List<string>> IgnoreFieldsByFile { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    public static string GetSettingsPath()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "DcfToSf2"
        );
        return Path.Combine(dir, "ui-settings.json");
    }

    public static UiSettings Load()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return new UiSettings();

            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<UiSettings>(json, JsonOptions);
            if (loaded is null)
                return new UiSettings();

            loaded.IgnoreFieldsByFile ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            loaded.IgnoreFields ??= [];

            // Re-create dictionary with case-insensitive comparer (JSON deserializer uses default)
            loaded.IgnoreFieldsByFile = new Dictionary<string, List<string>>(
                loaded.IgnoreFieldsByFile,
                StringComparer.OrdinalIgnoreCase
            );
            return loaded;
        }
        catch
        {
            return new UiSettings();
        }
    }

    public void Save()
    {
        try
        {
            string path = GetSettingsPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(this, JsonOptions));
        }
        catch
        {
            /* ignore persistence failures */
        }
    }

    public static string? NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            return Path.GetFullPath(path.Trim());
        }
        catch
        {
            return path.Trim();
        }
    }

    public HashSet<string> GetIgnoreFieldsFor(string? inputPath)
    {
        string? key = NormalizePath(inputPath);
        if (key is not null && IgnoreFieldsByFile.TryGetValue(key, out var list))
            return new HashSet<string>(list, StringComparer.OrdinalIgnoreCase);

        if (
            key is not null
            && string.Equals(key, NormalizePath(LastInputFile), StringComparison.OrdinalIgnoreCase)
        )
            return new HashSet<string>(IgnoreFields, StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    public void SetIgnoreFieldsFor(string? inputPath, IEnumerable<string> fields)
    {
        var list = fields
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();

        IgnoreFields = list;

        string? key = NormalizePath(inputPath);
        if (key is null)
            return;

        if (list.Count == 0)
            IgnoreFieldsByFile.Remove(key);
        else
            IgnoreFieldsByFile[key] = list;
    }
}
