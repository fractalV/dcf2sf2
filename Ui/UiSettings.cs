using System.Text.Json;
using DcfToSf2.Layout.Templates;

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
    public bool Modernize { get; set; }

    /// <summary>Last selected ignore fields (also used when no per-file entry).</summary>
    public List<string> IgnoreFields { get; set; } = [];

    /// <summary>Ignore fields keyed by normalized full path of the DCF file.</summary>
    public Dictionary<string, List<string>> IgnoreFieldsByFile { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Saved template slot geometry by template name.</summary>
    public Dictionary<string, List<TemplateSlotOverrideDto>> TemplateLayouts { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Template names whose saved layout replaces code defaults (not overlay).</summary>
    public List<string> TemplateLayoutsReplaceBase { get; set; } = [];

    /// <summary>User-created templates (from document tree branches).</summary>
    public List<UserTemplateDefinitionDto> UserTemplates { get; set; } = [];

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
        var loaded = ReadFile() ?? new UiSettings();
        ApplyStores(loaded);
        return loaded;
    }

    /// <summary>Write current in-memory stores to disk without reloading (wiping) them first.</summary>
    public static void PersistStores()
    {
        var settings = ReadFile() ?? new UiSettings();
        settings.Save();
    }

    private static UiSettings? ReadFile()
    {
        try
        {
            string path = GetSettingsPath();
            if (!File.Exists(path))
                return null;

            string json = File.ReadAllText(path);
            var loaded = JsonSerializer.Deserialize<UiSettings>(json, JsonOptions);
            if (loaded is null)
                return null;

            loaded.IgnoreFieldsByFile ??= new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            loaded.IgnoreFields ??= [];
            loaded.TemplateLayouts ??= new Dictionary<string, List<TemplateSlotOverrideDto>>(StringComparer.OrdinalIgnoreCase);
            loaded.TemplateLayoutsReplaceBase ??= [];
            loaded.UserTemplates ??= [];

            loaded.IgnoreFieldsByFile = new Dictionary<string, List<string>>(
                loaded.IgnoreFieldsByFile,
                StringComparer.OrdinalIgnoreCase
            );
            loaded.TemplateLayouts = new Dictionary<string, List<TemplateSlotOverrideDto>>(
                loaded.TemplateLayouts,
                StringComparer.OrdinalIgnoreCase
            );
            return loaded;
        }
        catch
        {
            return null;
        }
    }

    private static void ApplyStores(UiSettings loaded)
    {
        TemplateLayoutStore.Load(loaded.TemplateLayouts, loaded.TemplateLayoutsReplaceBase);
        UserTemplateStore.Load(loaded.UserTemplates);
    }

    public void Save()
    {
        try
        {
            TemplateLayouts = TemplateLayoutStore.ToDictionary();
            TemplateLayoutsReplaceBase = TemplateLayoutStore.ToReplaceBaseList();
            UserTemplates = UserTemplateStore.ToList();
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
