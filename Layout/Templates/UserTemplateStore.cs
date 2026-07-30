namespace DcfToSf2.Layout.Templates;

internal static class UserTemplateStore
{
    private static readonly object Gate = new();
    private static List<UserTemplateDefinitionDto> _defs = [];

    public static IReadOnlyList<UserTemplateDefinitionDto> Snapshot
    {
        get
        {
            lock (Gate)
                return _defs.Select(Clone).ToList();
        }
    }

    public static void Load(IEnumerable<UserTemplateDefinitionDto>? source)
    {
        lock (Gate)
        {
            _defs = [];
            if (source is null)
                return;
            foreach (var d in source)
            {
                if (string.IsNullOrWhiteSpace(d.Name) || d.Fields is null || d.Fields.Count == 0)
                    continue;
                _defs.Add(Clone(d));
            }
        }

        FieldGroupTemplateRegistry.Default.ReloadUserTemplates(Snapshot);
    }

    public static List<UserTemplateDefinitionDto> ToList()
    {
        lock (Gate)
            return _defs.Select(Clone).ToList();
    }

    public static bool Exists(string name)
    {
        lock (Gate)
            return _defs.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static string UniqueName(string desired)
    {
        string baseName = string.IsNullOrWhiteSpace(desired) ? "UserTemplate" : desired.Trim();
        if (!IsNameTaken(baseName))
            return baseName;

        for (int i = 2; i < 10_000; i++)
        {
            string candidate = $"{baseName}_{i}";
            if (!IsNameTaken(candidate))
                return candidate;
        }

        return $"{baseName}_{Guid.NewGuid():N}";
    }

    private static bool IsNameTaken(string name)
    {
        if (FieldGroupTemplateRegistry.Default.IsBuiltInName(name))
            return true;
        lock (Gate)
            return _defs.Any(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    public static UserDefinedFieldGroupTemplate Add(UserTemplateDefinitionDto definition)
    {
        var clone = Clone(definition);
        clone.Name = UniqueName(clone.Name);
        lock (Gate)
            _defs.Add(clone);

        FieldGroupTemplateRegistry.Default.ReloadUserTemplates(Snapshot);
        return new UserDefinedFieldGroupTemplate(clone);
    }

    public static bool Remove(string name)
    {
        lock (Gate)
        {
            int removed = _defs.RemoveAll(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (removed == 0)
                return false;
        }

        TemplateLayoutStore.ClearTemplate(name);
        FieldGroupTemplateRegistry.Default.ReloadUserTemplates(Snapshot);
        return true;
    }

    private static UserTemplateDefinitionDto Clone(UserTemplateDefinitionDto d) =>
        new()
        {
            Name = d.Name,
            MatchPath = d.MatchPath,
            MatchBySuffixes = d.MatchBySuffixes,
            Fields = d
                .Fields.Select(f => new UserTemplateFieldDto
                {
                    Suffix = f.Suffix,
                    Label = f.Label,
                    Width = f.Width,
                    Height = f.Height,
                })
                .ToList(),
        };
}
