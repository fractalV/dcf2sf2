using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DcfToSf2.Layout.Templates;

/// <summary>SF2-like text preview/edit for template slots (Data/Text/Pict editable).</summary>
internal static class TemplatePreviewText
{
    public const string RemovedPrefix = "REMOVED ";
    public const string IgnoredPrefix = "IGNORED ";

    private static readonly Regex LineRegex = new(
        @"^(?<type>Data|Text|Pict|Line|Rect)(?<x>\s*-?\d+)(?<y>\s*-?\d+)(?<w>\s*-?\d+)(?<h>\s*-?\d+)(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    public sealed record ApplyResult(int Updated, int Unmatched, IReadOnlyList<string> Messages);

    public static string Render(IReadOnlyList<TemplateSlot> slots, int maxWidth = 770)
    {
        // Text mode must mirror canvas slot geometry 1:1.
        // Do not route through TemplateSlotLayout.Apply (compaction / flow / pict reflow).
        _ = maxWidth;
        var list = EnsurePictSlotsForPreview(slots);

        var sb = new StringBuilder();
        foreach (
            var slot in list
                .Where(s => !s.IsIgnored && !s.IsRemoved)
                .OrderBy(s => s.Y)
                .ThenBy(s => s.X)
                .ThenBy(s => (int)s.Kind)
        )
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(FormatSlotLine(slot));
        }

        // REMOVED Data/Text can be restored from text; removed Picts are silent tombstones
        // (deleting their line must not bring "REMOVED Pict" back on Apply).
        foreach (
            var slot in slots.Where(s =>
                s.IsRemoved && !s.IsIgnored && s.Kind != TemplateSlotKind.Pict
            )
        )
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(RemovedPrefix);
            sb.Append(FormatSlotLine(slot));
        }

        // Ignored-from-list slots; omitted once dismissed (IsRemoved) via deleting IGNORED lines.
        foreach (var slot in slots.Where(s => s.IsIgnored && !s.IsRemoved))
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(IgnoredPrefix);
            sb.Append(FormatSlotLine(slot));
        }

        return sb.ToString();
    }

    /// <summary>Add missing default Picts for text preview without FixPictOverlaps (keeps Data X stable).</summary>
    private static List<TemplateSlot> EnsurePictSlotsForPreview(IReadOnlyList<TemplateSlot> slots)
    {
        var list = slots.Select(s => s.Clone()).ToList();
        var pictKeys = new HashSet<string>(
            list.Where(s => s.Kind == TemplateSlotKind.Pict).Select(s =>
                $"{s.Group}\0{s.Suffix}\0{s.StemHint}"
            ),
            StringComparer.OrdinalIgnoreCase
        );

        foreach (var data in list.Where(s => s.Kind == TemplateSlotKind.Data && !s.IsExcluded).ToList())
        {
            if (!LayoutUtil.ShouldAddPict(data.Suffix))
                continue;
            string key = $"{data.Group}\0{data.Suffix}\0{data.StemHint}";
            if (pictKeys.Contains(key))
                continue;

            list.Add(TemplateSlotLayout.CreateDefaultPictSlot(data));
            pictKeys.Add(key);
        }

        return list;
    }

    public static ApplyResult ApplyToSlots(string text, IList<TemplateSlot> slots)
    {
        var messages = new List<string>();
        int updated = 0;
        int unmatched = 0;

        // Include removed/ignored so prefixed lines and geometry sync still work.
        var dataSlots = slots.Where(s => s.Kind == TemplateSlotKind.Data).ToList();
        var textSlots = slots.Where(s => s.Kind == TemplateSlotKind.Text).ToList();
        var pictSlots = slots.Where(s => s.Kind == TemplateSlotKind.Pict).ToList();
        var usedData = new HashSet<TemplateSlot>();
        var usedText = new HashSet<TemplateSlot>();
        var usedPict = new HashSet<TemplateSlot>();

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            // Strip status prefixes (order: REMOVED then IGNORED, or vice versa).
            bool markedRemoved = false;
            for (int i = 0; i < 2; i++)
            {
                if (line.StartsWith(RemovedPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    markedRemoved = true;
                    line = line[RemovedPrefix.Length..].TrimStart();
                }
                else if (line.StartsWith(IgnoredPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    line = line[IgnoredPrefix.Length..].TrimStart();
                }
                else
                    break;
            }

            if (!TryParseLine(line, out var parsed))
            {
                messages.Add($"Не разобрана строка: {Truncate(raw.TrimEnd())}");
                unmatched++;
                continue;
            }

            if (parsed.Type is Type.Line or Type.Rect)
                continue;

            int slotX = parsed.X;
            int slotY = parsed.Y;
            int slotW = Math.Max(parsed.Type == Type.Pict ? LayoutUtil.PictSize : 16, parsed.Width);
            int slotH = Math.Max(parsed.Type == Type.Pict ? LayoutUtil.PictSize : 8, parsed.Height);
            if (parsed.Type == Type.Pict)
            {
                slotW = LayoutUtil.PictSize;
                slotH = LayoutUtil.PictSize;
            }

            if (parsed.Type == Type.Pict)
            {
                var slot = MatchPictSlot(pictSlots, usedPict, parsed.ValueName);
                if (slot is null)
                {
                    messages.Add($"Нет слота для Pict: {parsed.ValueName}");
                    unmatched++;
                    continue;
                }

                usedPict.Add(slot);
                slot.X = slotX;
                slot.Y = slotY;
                slot.Width = LayoutUtil.PictSize;
                slot.Height = LayoutUtil.PictSize;
                ApplyStatusFlags(slot, markedRemoved);
                if (!string.IsNullOrEmpty(parsed.ValueName))
                    slot.Label = parsed.ValueName;
                updated++;
                continue;
            }

            if (parsed.Type == Type.Data)
            {
                var slot = MatchDataSlot(dataSlots, usedData, parsed.ValueName);
                if (slot is null)
                {
                    messages.Add($"Нет слота для Data: {parsed.ValueName}");
                    unmatched++;
                    continue;
                }

                usedData.Add(slot);
                slot.X = Math.Max(0, slotX);
                slot.Y = Math.Max(0, slotY);
                slot.Width = slotW;
                slot.Height = slotH;
                ApplyStatusFlags(slot, markedRemoved);
                slot.Comment = parsed.Comment;
                updated++;
                continue;
            }

            if (parsed.Type == Type.Text)
            {
                string label = parsed.TextLabel;
                var slot = MatchTextSlot(textSlots, usedText, label, slotX, slotY);
                if (slot is null)
                {
                    if (markedRemoved || string.IsNullOrWhiteSpace(label))
                    {
                        messages.Add($"Нет слота для Text: {Truncate(label)}");
                        unmatched++;
                        continue;
                    }

                    slot = CreateUserTextSlot(slots, label, slotX, slotY, slotW, slotH);
                    textSlots.Add(slot);
                }

                usedText.Add(slot);
                slot.X = slotX;
                slot.Y = slotY;
                slot.Width = slotW;
                slot.Height = slotH;
                if (!string.IsNullOrWhiteSpace(label))
                    slot.Label = label;

                ApplyStatusFlags(slot, markedRemoved);
                updated++;
            }
        }

        // Deleting an IGNORED line dismisses that slot from the template (same as remove).
        foreach (var slot in slots)
        {
            if (!slot.IsIgnored)
                continue;
            bool used =
                usedData.Contains(slot) || usedText.Contains(slot) || usedPict.Contains(slot);
            if (!used)
                slot.IsRemoved = true;
        }

        // Deleting a Pict line removes it (tombstone). Unlike Data/Text, missing Picts are
        // auto-recreated by EnsurePictSlots unless IsRemoved is set.
        foreach (var slot in pictSlots)
        {
            if (slot.IsIgnored || usedPict.Contains(slot))
                continue;
            slot.IsRemoved = true;
        }

        return new ApplyResult(updated, unmatched, messages);
    }

    private static TemplateSlot CreateUserTextSlot(
        IList<TemplateSlot> slots,
        string label,
        int x,
        int y,
        int width,
        int height
    )
    {
        string group =
            slots.Select(s => s.Group).FirstOrDefault(g => !string.IsNullOrEmpty(g)) ?? "Custom";
        string key = NextUserTextKey(slots);
        var slot = TemplateSlotLayout.TextSlot(
            group,
            label,
            x,
            y,
            Math.Max(16, width),
            Math.Max(8, height),
            key
        );
        slot.IsEmbedded = true;
        slots.Add(slot);
        return slot;
    }

    internal static string NextUserTextKey(IEnumerable<TemplateSlot> slots)
    {
        var used = new HashSet<string>(
            slots.Where(s => s.Kind == TemplateSlotKind.Text).Select(s => s.Suffix),
            StringComparer.OrdinalIgnoreCase
        );
        for (int n = 1; n < 100_000; n++)
        {
            string key = $"usertext:{n}";
            if (!used.Contains(key))
                return key;
        }

        return $"usertext:{Guid.NewGuid():N}";
    }

    /// <summary>
    /// IGNORED lines keep IsIgnored from the ignore-list; presence restores a dismissed slot.
    /// REMOVED applies only to non-ignored slots.
    /// </summary>
    private static void ApplyStatusFlags(TemplateSlot slot, bool markedRemoved)
    {
        if (slot.IsIgnored)
            slot.IsRemoved = false;
        else
            slot.IsRemoved = markedRemoved;
    }

    private static string FormatSlotLine(TemplateSlot slot)
    {
        string typeName = slot.Kind switch
        {
            TemplateSlotKind.Text => "Text",
            TemplateSlotKind.Pict => "Pict",
            _ => "Data",
        };
        var sb = new StringBuilder();
        sb.Append(typeName);
        sb.Append($"{slot.X,12}");
        sb.Append($"{slot.Y,6}");
        sb.Append($"{slot.Width,6}");
        sb.Append($"{slot.Height,5}");
        sb.Append(' ');
        if (slot.Kind == TemplateSlotKind.Text)
        {
            string label = string.IsNullOrEmpty(slot.Label) ? slot.Suffix : slot.Label;
            sb.Append(Consts.SpecialDelimetr);
            sb.Append(label);
        }
        else if (slot.Kind == TemplateSlotKind.Pict)
        {
            string pictValue = string.IsNullOrEmpty(slot.Label)
                ? TemplateSlotLayout.PictFieldName(slot)
                : slot.Label;
            sb.Append(Consts.SpecialDelimetr);
            sb.Append(pictValue);
        }
        else
        {
            sb.Append(TemplateSlotLayout.PictFieldName(slot));
            if (!string.IsNullOrEmpty(slot.Comment))
            {
                sb.Append(' ');
                sb.Append(Consts.SpecialDelimetr);
                sb.Append(slot.Comment);
            }
        }

        return sb.ToString();
    }

    private static TemplateSlot? MatchPictSlot(
        List<TemplateSlot> candidates,
        HashSet<TemplateSlot> used,
        string pictValue
    )
    {
        string name = pictValue;
        int eq = name.IndexOf('=');
        if (eq > 0)
            name = name[..eq];

        string last = LayoutUtil.GetLastToken(name);
        var matches = candidates
            .Where(s =>
                !used.Contains(s)
                && (
                    s.Suffix.Equals(last, StringComparison.OrdinalIgnoreCase)
                    || (!string.IsNullOrEmpty(s.Label) && s.Label.Equals(pictValue, StringComparison.OrdinalIgnoreCase))
                    || TemplateSlotLayout.PictFieldName(s).Equals(name, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();
        if (matches.Count == 0)
            return null;
        if (matches.Count == 1)
            return matches[0];

        var hinted = matches.FirstOrDefault(s =>
            !string.IsNullOrEmpty(s.StemHint)
            && name.Contains(s.StemHint, StringComparison.OrdinalIgnoreCase)
        );
        return hinted ?? matches[0];
    }

    private static TemplateSlot? MatchDataSlot(
        List<TemplateSlot> candidates,
        HashSet<TemplateSlot> used,
        string fieldName
    )
    {
        string last = LayoutUtil.GetLastToken(fieldName);
        var matches = candidates
            .Where(s => !used.Contains(s) && s.Suffix.Equals(last, StringComparison.OrdinalIgnoreCase))
            .ToList();
        if (matches.Count == 0)
            return null;
        if (matches.Count == 1)
            return matches[0];

        var hinted = matches.FirstOrDefault(s =>
            !string.IsNullOrEmpty(s.StemHint)
            && fieldName.Contains(s.StemHint, StringComparison.OrdinalIgnoreCase)
        );
        if (hinted is not null)
            return hinted;

        var exact = matches.FirstOrDefault(s =>
        {
            string n = string.IsNullOrEmpty(s.StemHint) ? s.Suffix : $"{s.StemHint}_{s.Suffix}";
            return n.Equals(fieldName, StringComparison.OrdinalIgnoreCase);
        });
        return exact ?? matches[0];
    }

    private static TemplateSlot? MatchTextSlot(
        List<TemplateSlot> candidates,
        HashSet<TemplateSlot> used,
        string label,
        int x,
        int y
    )
    {
        var matches = candidates
            .Where(s =>
                !used.Contains(s)
                && (
                    s.Label.Equals(label, StringComparison.Ordinal)
                    || s.Suffix.Equals(label, StringComparison.OrdinalIgnoreCase)
                )
            )
            .ToList();
        if (matches.Count == 0)
            return null;
        if (matches.Count == 1)
            return matches[0];

        return matches.OrderBy(s => Math.Abs(s.X - x) + Math.Abs(s.Y - y)).First();
    }

    private readonly record struct ParsedLine(
        Type Type,
        int X,
        int Y,
        int Width,
        int Height,
        string ValueName,
        string TextLabel,
        string Comment
    );

    private static bool TryParseLine(string line, out ParsedLine parsed)
    {
        parsed = default;
        var m = LineRegex.Match(line);
        if (!m.Success)
            return false;

        string typeName = m.Groups["type"].Value;
        Type type = typeName switch
        {
            "Data" => Type.Data,
            "Text" => Type.Text,
            "Pict" => Type.Pict,
            "Line" => Type.Line,
            "Rect" => Type.Rect,
            _ => Type.Data,
        };
        if (
            !int.TryParse(m.Groups["x"].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int x)
            || !int.TryParse(m.Groups["y"].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int y)
            || !int.TryParse(m.Groups["w"].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int w)
            || !int.TryParse(m.Groups["h"].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int h)
        )
            return false;

        string rest = m.Groups["rest"].Value.TrimStart();
        string valueName = string.Empty;
        string textLabel = string.Empty;
        string comment = string.Empty;

        if (type == Type.Data)
        {
            int tick = rest.IndexOf(Consts.SpecialDelimetr);
            valueName = (tick >= 0 ? rest[..tick] : rest).Trim();
            if (tick >= 0)
                comment = rest[(tick + 1)..].Trim();
            int space = valueName.IndexOf(' ');
            if (space > 0)
                valueName = valueName[..space];
        }
        else if (type == Type.Text)
        {
            textLabel = rest.TrimStart(Consts.SpecialDelimetr).Trim();
        }
        else if (type == Type.Pict)
        {
            valueName = rest.TrimStart(Consts.SpecialDelimetr).Trim();
        }

        parsed = new ParsedLine(type, x, y, w, h, valueName, textLabel, comment);
        return true;
    }

    private static string Truncate(string s) =>
        s.Length <= 60 ? s : string.Concat(s.AsSpan(0, 57), "...");
}
