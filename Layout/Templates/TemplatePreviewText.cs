using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DcfToSf2.Layout.Templates;

/// <summary>SF2-like text preview/edit for template slots (Data/Text/Pict editable).</summary>
internal static class TemplatePreviewText
{
    public const string RemovedPrefix = "REMOVED ";

    private static readonly Regex LineRegex = new(
        @"^(?<type>Data|Text|Pict|Line|Rect)(?<x>\s*-?\d+)(?<y>\s*-?\d+)(?<w>\s*-?\d+)(?<h>\s*-?\d+)(?<rest>.*)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant
    );

    public sealed record ApplyResult(int Updated, int Unmatched, IReadOnlyList<string> Messages);

    public static string Render(IReadOnlyList<TemplateSlot> slots, int maxWidth = 770)
    {
        var fields = BuildSyntheticFields(slots);
        var block = new Block { Name = "PREVIEW" };
        TemplateSlotLayout.Apply(
            block,
            fields,
            slots,
            Consts.Default_X,
            Consts.Default_Y,
            maxWidth,
            addPict: true
        );

        var sb = new StringBuilder();
        string active = block.ToString().TrimEnd();
        if (!string.IsNullOrEmpty(active))
            sb.Append(active);

        foreach (var slot in slots.Where(s => s.IsRemoved && !s.IsIgnored))
        {
            if (sb.Length > 0)
                sb.AppendLine();
            sb.Append(RemovedPrefix);
            sb.Append(FormatSlotLine(slot));
        }

        return sb.ToString();
    }

    public static ApplyResult ApplyToSlots(string text, IList<TemplateSlot> slots)
    {
        var messages = new List<string>();
        int updated = 0;
        int unmatched = 0;

        // Include removed so REMOVED lines and restore via plain Data/Text both work.
        var dataSlots = slots
            .Where(s => s.Kind == TemplateSlotKind.Data && !s.IsIgnored)
            .ToList();
        var textSlots = slots
            .Where(s => s.Kind == TemplateSlotKind.Text && !s.IsIgnored)
            .ToList();
        var pictSlots = slots
            .Where(s => s.Kind == TemplateSlotKind.Pict && !s.IsIgnored)
            .ToList();
        var usedData = new HashSet<TemplateSlot>();
        var usedText = new HashSet<TemplateSlot>();
        var usedPict = new HashSet<TemplateSlot>();

        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            string line = raw.TrimEnd();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            bool markedRemoved = false;
            if (line.StartsWith(RemovedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                markedRemoved = true;
                line = line[RemovedPrefix.Length..].TrimStart();
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
                slot.IsRemoved = markedRemoved;
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
                slot.X = slotX;
                slot.Y = slotY;
                slot.Width = slotW;
                slot.Height = slotH;
                slot.IsRemoved = markedRemoved;
                updated++;
                continue;
            }

            if (parsed.Type == Type.Text)
            {
                string label = parsed.TextLabel;
                var slot = MatchTextSlot(textSlots, usedText, label, slotX, slotY);
                if (slot is null)
                {
                    messages.Add($"Нет слота для Text: {Truncate(label)}");
                    unmatched++;
                    continue;
                }

                usedText.Add(slot);
                slot.X = slotX;
                slot.Y = slotY;
                slot.Width = slotW;
                slot.Height = slotH;
                slot.IsRemoved = markedRemoved;
                updated++;
            }
        }

        return new ApplyResult(updated, unmatched, messages);
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
        }

        return sb.ToString();
    }

    private static List<Data> BuildSyntheticFields(IReadOnlyList<TemplateSlot> slots)
    {
        var fields = new List<Data>();
        foreach (var slot in slots)
        {
            if (slot.Kind != TemplateSlotKind.Data || slot.IsExcluded)
                continue;

            string name = string.IsNullOrEmpty(slot.StemHint)
                ? slot.Suffix
                : $"{slot.StemHint}_{slot.Suffix}";
            bool isDate = slot.Suffix.EndsWith("Date", StringComparison.OrdinalIgnoreCase);
            string value = isDate ? "D10 |" : $"C{Math.Max(1, slot.Width / Consts.DefaultWidthChar)} |";
            fields.Add(
                new Data
                {
                    Level = 0,
                    NameBlock = "PREVIEW",
                    Path = "PREVIEW",
                    FieldElement = new Element { ElementName = name, ElementValue = value },
                    SourceLine = 0,
                }
            );
        }

        return fields;
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
        string TextLabel
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

        if (type == Type.Data)
        {
            int tick = rest.IndexOf(Consts.SpecialDelimetr);
            valueName = (tick >= 0 ? rest[..tick] : rest).Trim();
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

        parsed = new ParsedLine(type, x, y, w, h, valueName, textLabel);
        return true;
    }

    private static string Truncate(string s) =>
        s.Length <= 60 ? s : string.Concat(s.AsSpan(0, 57), "...");
}
