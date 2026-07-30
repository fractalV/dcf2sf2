namespace DcfToSf2.Layout.Templates;

/// <summary>Persisted per-slot geometry overrides for templates.</summary>
internal sealed class TemplateSlotOverrideDto
{
    public string Id { get; set; } = string.Empty;
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public bool Removed { get; set; }

    /// <summary>True when the slot was dragged in from another template (full metadata required).</summary>
    public bool Embedded { get; set; }

    public string? Suffix { get; set; }
    public string? Label { get; set; }
    public TemplateSlotKind Kind { get; set; }
    public string? Group { get; set; }
    public string? StemHint { get; set; }
}

internal static class TemplateLayoutStore
{
    private static readonly object Gate = new();
    private static Dictionary<string, List<TemplateSlotOverrideDto>> _byTemplate =
        new(StringComparer.OrdinalIgnoreCase);
    private static HashSet<string> _replaceBase = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyDictionary<string, List<TemplateSlotOverrideDto>> Snapshot
    {
        get
        {
            lock (Gate)
                return new Dictionary<string, List<TemplateSlotOverrideDto>>(_byTemplate, StringComparer.OrdinalIgnoreCase);
        }
    }

    public static IReadOnlyCollection<string> ReplaceBaseTemplates
    {
        get
        {
            lock (Gate)
                return _replaceBase.ToList();
        }
    }

    public static void Load(
        Dictionary<string, List<TemplateSlotOverrideDto>>? source,
        IEnumerable<string>? replaceBase = null
    )
    {
        lock (Gate)
        {
            _byTemplate = new Dictionary<string, List<TemplateSlotOverrideDto>>(StringComparer.OrdinalIgnoreCase);
            _replaceBase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (replaceBase is not null)
            {
                foreach (var name in replaceBase)
                {
                    if (!string.IsNullOrWhiteSpace(name))
                        _replaceBase.Add(name.Trim());
                }
            }

            if (source is null)
                return;
            foreach (var (key, list) in source)
            {
                if (string.IsNullOrWhiteSpace(key) || list is null)
                    continue;
                _byTemplate[key] = list
                    .Where(s => !string.IsNullOrWhiteSpace(s.Id))
                    .Select(CloneDto)
                    .ToList();
            }
        }
    }

    private static int Snap(int value)
    {
        const int step = 4;
        return (int)(Math.Round(value / (double)step) * step);
    }

    private static TemplateSlotOverrideDto CloneDto(TemplateSlotOverrideDto s)
    {
        bool pict = s.Kind == TemplateSlotKind.Pict || s.Id.StartsWith("Pict:", StringComparison.OrdinalIgnoreCase);
        return new()
        {
            Id = s.Id,
            X = Snap(s.X),
            Y = Snap(s.Y),
            Width = pict ? LayoutUtil.PictSize : Math.Max(16, Snap(s.Width)),
            Height = pict ? LayoutUtil.PictSize : Math.Max(8, Snap(s.Height)),
            Removed = s.Removed,
            Embedded = s.Embedded,
            Suffix = s.Suffix,
            Label = s.Label,
            Kind = s.Kind,
            Group = s.Group,
            StemHint = s.StemHint,
        };
    }

    public static Dictionary<string, List<TemplateSlotOverrideDto>> ToDictionary()
    {
        lock (Gate)
        {
            return _byTemplate.ToDictionary(
                kv => kv.Key,
                kv => kv.Value.Select(CloneDto).ToList(),
                StringComparer.OrdinalIgnoreCase
            );
        }
    }

    public static List<string> ToReplaceBaseList()
    {
        lock (Gate)
            return _replaceBase.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static bool HasOverrides(string templateName)
    {
        lock (Gate)
            return _byTemplate.TryGetValue(templateName, out var list) && list.Count > 0;
    }

    public static bool IsReplaceBase(string templateName)
    {
        lock (Gate)
            return _replaceBase.Contains(templateName);
    }

    /// <param name="replaceBase">
    /// When true, removed slots are dropped and the remaining layout becomes the new base
    /// (full metadata). When false and the template is not already a replaced base, saves as overrides.
    /// </param>
    public static void SaveTemplate(
        string templateName,
        IEnumerable<TemplateSlot> slots,
        bool replaceBase = false
    )
    {
        var slotList = slots.Where(s => !string.IsNullOrEmpty(s.Id)).ToList();
        bool asBase;
        lock (Gate)
            asBase = replaceBase || _replaceBase.Contains(templateName);

        if (replaceBase)
            slotList = slotList.Where(s => !s.IsRemoved).ToList();

        var list = slotList.Select(s => ToDto(s, forceFullMetadata: asBase)).ToList();

        lock (Gate)
        {
            if (list.Count == 0)
            {
                _byTemplate.Remove(templateName);
                _replaceBase.Remove(templateName);
            }
            else
            {
                _byTemplate[templateName] = list;
                if (replaceBase)
                    _replaceBase.Add(templateName);
                else if (!asBase)
                    _replaceBase.Remove(templateName);
            }
        }
    }

    private static TemplateSlotOverrideDto ToDto(TemplateSlot s, bool forceFullMetadata)
    {
        bool full = s.IsEmbedded || forceFullMetadata || s.Kind == TemplateSlotKind.Pict;
        bool pict = s.Kind == TemplateSlotKind.Pict;
        var dto = new TemplateSlotOverrideDto
        {
            Id = s.Id,
            X = Snap(s.X),
            Y = Snap(s.Y),
            Width = pict ? LayoutUtil.PictSize : Math.Max(16, Snap(s.Width)),
            Height = pict ? LayoutUtil.PictSize : Math.Max(8, Snap(s.Height)),
            Removed = s.IsRemoved,
            Embedded = full,
        };

        if (full)
        {
            dto.Suffix = s.Suffix;
            dto.Label = s.Label;
            dto.Kind = s.Kind;
            dto.Group = s.Group;
            dto.StemHint = string.IsNullOrEmpty(s.StemHint) ? null : s.StemHint;
        }

        return dto;
    }

    public static void ClearTemplate(string templateName)
    {
        lock (Gate)
        {
            _byTemplate.Remove(templateName);
            _replaceBase.Remove(templateName);
        }
    }

    public static IReadOnlyList<TemplateSlot> ApplyOverrides(
        string templateName,
        IReadOnlyList<TemplateSlot> defaults
    )
    {
        var expanded = TemplateSlotLayout.EnsurePictSlots(defaults);

        List<TemplateSlotOverrideDto>? overrides;
        bool replaceBase;
        lock (Gate)
        {
            if (!_byTemplate.TryGetValue(templateName, out overrides) || overrides.Count == 0)
                return expanded;
            overrides = overrides.ToList();
            replaceBase = _replaceBase.Contains(templateName);
        }

        if (replaceBase)
            return TemplateSlotLayout.EnsurePictSlots(MaterializeBase(overrides, expanded));

        var byId = overrides.ToDictionary(o => o.Id, StringComparer.OrdinalIgnoreCase);
        var defaultIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<TemplateSlot>(expanded.Count);
        foreach (var d in expanded)
        {
            var slot = d.Clone();
            if (string.IsNullOrEmpty(slot.Id))
                slot.Id = TemplateSlot.MakeId(slot.Kind, slot.Group, slot.Suffix, slot.StemHint);
            defaultIds.Add(slot.Id);
            if (byId.TryGetValue(slot.Id, out var o))
            {
                slot.X = Snap(o.X);
                slot.Y = Snap(o.Y);
                if (slot.Kind == TemplateSlotKind.Pict)
                {
                    slot.Width = LayoutUtil.PictSize;
                    slot.Height = LayoutUtil.PictSize;
                }
                else
                {
                    slot.Width = Math.Max(16, Snap(o.Width));
                    slot.Height = Math.Max(8, Snap(o.Height));
                }

                slot.IsRemoved = o.Removed;
            }

            result.Add(slot);
        }

        foreach (var o in overrides)
        {
            if (defaultIds.Contains(o.Id))
                continue;
            if (!CanMaterializeEmbedded(o))
                continue;
            if (result.Any(s => s.Id.Equals(o.Id, StringComparison.OrdinalIgnoreCase)))
                continue;

            result.Add(FromEmbeddedDto(o));
        }

        return result;
    }

    /// <summary>Full base replacement: use saved slots; keep IsIgnored from matching defaults when possible.</summary>
    private static List<TemplateSlot> MaterializeBase(
        List<TemplateSlotOverrideDto> overrides,
        IReadOnlyList<TemplateSlot> defaults
    )
    {
        var ignoredById = defaults
            .Where(d => d.IsIgnored && !string.IsNullOrEmpty(d.Id))
            .ToDictionary(d => d.Id, d => true, StringComparer.OrdinalIgnoreCase);
        var ignoredBySuffix = defaults
            .Where(d => d.IsIgnored)
            .GroupBy(d => d.Suffix, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => true, StringComparer.OrdinalIgnoreCase);

        var result = new List<TemplateSlot>();
        foreach (var o in overrides)
        {
            if (!CanMaterializeEmbedded(o) && string.IsNullOrWhiteSpace(o.Suffix))
                continue;

            var slot = FromEmbeddedDto(o);
            if (ignoredById.ContainsKey(slot.Id) || ignoredBySuffix.ContainsKey(slot.Suffix))
                slot.IsIgnored = true;
            result.Add(slot);
        }

        return result;
    }

    private static bool CanMaterializeEmbedded(TemplateSlotOverrideDto o) =>
        o.Embedded
        || (
            !string.IsNullOrWhiteSpace(o.Suffix)
            && !string.IsNullOrWhiteSpace(o.Group)
        );

    private static TemplateSlot FromEmbeddedDto(TemplateSlotOverrideDto o)
    {
        string suffix = o.Suffix ?? string.Empty;
        string group = o.Group ?? string.Empty;
        string stem = o.StemHint ?? string.Empty;
        string id = string.IsNullOrEmpty(o.Id)
            ? TemplateSlot.MakeId(o.Kind, group, suffix, stem)
            : o.Id;

        return new TemplateSlot
        {
            Id = id,
            Suffix = suffix,
            Label = o.Label ?? string.Empty,
            Kind = o.Kind,
            X = Snap(o.X),
            Y = Snap(o.Y),
            Width = o.Kind == TemplateSlotKind.Pict ? LayoutUtil.PictSize : Math.Max(16, Snap(o.Width)),
            Height = o.Kind == TemplateSlotKind.Pict ? LayoutUtil.PictSize : Math.Max(8, Snap(o.Height)),
            IsRemoved = o.Removed,
            IsEmbedded = true,
            Group = group,
            StemHint = stem,
        };
    }
}
