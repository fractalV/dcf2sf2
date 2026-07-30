using System.Text;
using System.Text.RegularExpressions;
using DcfToSf2.Diagnostics;

namespace DcfToSf2.Parsing;

internal static class DcfParser
{
    private static readonly HashSet<string> BuiltInServiceFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "DocumentID",
        "RefDocumentID",
        "MCD_ID",
        "INNSign",
        "ITNReserv",
        "OKTMO",
        "KLADR",
        "AOGUID",
        "AOID",
        "TerritoryCode",
    };

    public static DcfParseResult Parse(FileInfo file)
    {
        var diagnostics = new DcfDiagnostics();
        string text = ReadCp1251(file);
        var header = ParseHeader(text, diagnostics);
        var fields = ParseFields(text, diagnostics, header);
        var labels = XsdHints.LoadLabels(file, diagnostics);

        if (string.IsNullOrEmpty(header.DocumentName) && fields.Count > 0)
            header.DocumentName = GuessDocumentName(text);

        if (
            !string.IsNullOrEmpty(header.MainBlock)
            && !fields.Any(f =>
                string.Equals(f.NameBlock, header.MainBlock, StringComparison.OrdinalIgnoreCase)
                || string.Equals(f.FieldElement.ElementName, header.MainBlock, StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            diagnostics.Warn(
                DiagnosticCategory.Structure,
                $"MainBlock '{header.MainBlock}' from Header was not found in Fields"
            );
        }

        return new DcfParseResult
        {
            Header = header,
            Fields = fields,
            Diagnostics = diagnostics,
            XsdLabels = labels,
        };
    }

    public static bool IsBuiltInServiceField(string elementName)
    {
        if (BuiltInServiceFields.Contains(elementName))
            return true;

        string last = GetLastToken(elementName);
        return BuiltInServiceFields.Contains(last);
    }

    public static bool ShouldSkipField(string elementName, HashSet<string> ignoreFields)
    {
        if (IsBuiltInServiceField(elementName))
            return true;

        if (ignoreFields.Count == 0)
            return false;

        if (ignoreFields.Contains(elementName))
            return true;

        return ignoreFields.Contains(GetLastToken(elementName));
    }

    public static string GetLastToken(string elementName)
    {
        var parts = elementName.Split('_', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[^1] : elementName;
    }

    private static string ReadCp1251(FileInfo file)
    {
        byte[] bytes = File.ReadAllBytes(file.FullName);
        return Encoding.GetEncoding(1251).GetString(bytes);
    }

    private static DcfHeader ParseHeader(string text, DcfDiagnostics diagnostics)
    {
        var header = new DcfHeader();
        var headerMatch = Regex.Match(text, @"\[(?<name>[^\]]+)\.Header\]", RegexOptions.IgnoreCase);
        if (!headerMatch.Success)
        {
            diagnostics.Warn(DiagnosticCategory.Structure, "Header section [*.Header] not found");
            header.DocumentName = GuessDocumentName(text);
            return header;
        }

        header.DocumentName = headerMatch.Groups["name"].Value.Trim();
        int start = headerMatch.Index + headerMatch.Length;
        var fieldsMatch = Regex.Match(text[start..], @"\[[^\]]+\.Fields\]", RegexOptions.IgnoreCase);
        string headerBody = fieldsMatch.Success ? text.Substring(start, fieldsMatch.Index) : text[start..];

        foreach (var rawLine in headerBody.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.StartsWith(';') || !line.Contains('='))
                continue;

            int eq = line.IndexOf('=');
            string key = line[..eq].Trim();
            string value = line[(eq + 1)..].Trim();

            switch (key.ToLowerInvariant())
            {
                case "shortname":
                    header.ShortName = value;
                    break;
                case "fullname":
                    header.FullName = value;
                    break;
                case "mainblock":
                    header.MainBlock = value;
                    break;
                case "keyfield":
                    header.KeyField = value;
                    break;
                case "datefield":
                    header.DateField = value;
                    break;
                case "constrmain":
                    header.ConstrMain = value;
                    break;
                case "constrblock":
                    header.ConstrBlock = value;
                    break;
                case "gcodefield":
                    header.GCodeField = value;
                    break;
            }
        }

        return header;
    }

    private static List<Data> ParseFields(string text, DcfDiagnostics diagnostics, DcfHeader header)
    {
        const string marker = ".Fields]";
        int idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
        {
            diagnostics.Error(DiagnosticCategory.Structure, "section [*.Fields] not found");
            return [];
        }

        // verify opening bracket exists before .Fields]
        int bracket = text.LastIndexOf('[', idx);
        if (bracket < 0)
            diagnostics.Error(DiagnosticCategory.Structure, "malformed Fields section header");

        string body = text[(idx + marker.Length)..];
        // Normalize newlines
        var lines = body.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');

        List<Data> elements = [];
        int level = 0;
        string rusName = string.Empty;
        var stack = new Stack<string>();
        var namesAtLevel = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < lines.Length; i++)
        {
            int lineNo = CountLinesBefore(text, idx + marker.Length) + i + 1;
            string item = lines[i].TrimStart();
            if (string.IsNullOrWhiteSpace(item))
                continue;

            // Strip PATTERN=/NAMESPACE= for layout, but validate quotes first on original
            string original = item;
            ValidateAttributes(original, diagnostics, lineNo);

            item = StripMeta(item);

            if (item.TrimStart().StartsWith(';'))
            {
                rusName = item.Replace("; ====>", "", StringComparison.OrdinalIgnoreCase).Trim().TrimStart(';').Trim();
                continue;
            }

            if (item.Contains('='))
            {
                int eq = item.IndexOf('=');
                string name = item[..eq].Trim();
                string value = item[(eq + 1)..].Trim();

                // Detect block open on same line: Name{&&Num ...
                if (name.Contains('{'))
                {
                    ParseBlockOpen(name, value, ref level, stack, elements, rusName, diagnostics, lineNo);
                    continue;
                }

                var parsedType = FieldTypeParser.Parse(value);
                if (!string.IsNullOrEmpty(value) && !parsedType.IsValid && !value.TrimStart().StartsWith('{'))
                {
                    string token = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
                    diagnostics.Warn(
                        DiagnosticCategory.FieldType,
                        $"cannot parse type token '{token}'",
                        lineNo,
                        name
                    );
                }

                ValidateSeqChoice(value, diagnostics, lineNo, name);

                string blockName = stack.Count > 0 ? stack.Peek() : Consts.DefaultBlockName;
                string path = BuildPath(stack);
                TrackDuplicate(namesAtLevel, path, name, diagnostics, lineNo);

                elements.Add(
                    new Data
                    {
                        Level = level,
                        NameBlock = blockName,
                        Path = path,
                        RusNameBlock = rusName,
                        SourceLine = lineNo,
                        FieldElement = new Element { ElementName = name, ElementValue = value },
                    }
                );
                continue;
            }

            if (item.Contains('{'))
            {
                string name = item[..item.IndexOf('{')].Trim();
                level++;
                stack.Push(string.IsNullOrEmpty(name) ? $"Block{level}" : name);
                string path = BuildPath(stack);
                elements.Add(
                    new Data
                    {
                        Level = level,
                        NameBlock = stack.Peek(),
                        Path = path,
                        RusNameBlock = rusName,
                        SourceLine = lineNo,
                        FieldElement = new Element { ElementName = stack.Peek(), ElementValue = string.Empty },
                    }
                );
                continue;
            }

            if (item.Trim().StartsWith('}'))
            {
                if (level <= 0 || stack.Count == 0)
                {
                    diagnostics.Warn(
                        DiagnosticCategory.Nesting,
                        $"unmatched '}}' while path={CurrentPath(stack)}",
                        lineNo
                    );
                }
                else
                {
                    level--;
                    stack.Pop();
                }
            }
        }

        if (level > 0 || stack.Count > 0)
        {
            diagnostics.Warn(
                DiagnosticCategory.Nesting,
                $"unclosed block(s) at end of Fields, remaining path={CurrentPath(stack)}"
            );
        }

        return elements;
    }

    private static void ParseBlockOpen(
        string nameWithBrace,
        string value,
        ref int level,
        Stack<string> stack,
        List<Data> elements,
        string rusName,
        DcfDiagnostics diagnostics,
        int lineNo
    )
    {
        int brace = nameWithBrace.IndexOf('{');
        string name = nameWithBrace[..brace].Trim();
        if (string.IsNullOrEmpty(name))
        {
            diagnostics.Warn(DiagnosticCategory.Structure, "block declaration without name", lineNo);
            name = $"Block{level + 1}";
        }

        level++;
        stack.Push(name);
        string path = BuildPath(stack);
        elements.Add(
            new Data
            {
                Level = level,
                NameBlock = name,
                Path = path,
                RusNameBlock = rusName,
                SourceLine = lineNo,
                FieldElement = new Element { ElementName = name, ElementValue = string.Empty },
            }
        );
    }

    private static string BuildPath(Stack<string> stack)
    {
        if (stack.Count == 0)
            return Consts.DefaultBlockName;
        // Top-level repeating blocks use their own name as SF2 section (Goods, InvoiceGoods)
        return string.Join("\\", stack.Reverse());
    }

    private static void TrackDuplicate(
        Dictionary<string, HashSet<string>> map,
        string block,
        string field,
        DcfDiagnostics diagnostics,
        int lineNo
    )
    {
        if (!map.TryGetValue(block, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            map[block] = set;
        }

        if (!set.Add(field))
        {
            diagnostics.Warn(
                DiagnosticCategory.Structure,
                $"duplicate field name '{field}' in block '{block}'",
                lineNo,
                field
            );
        }
    }

    private static void ValidateAttributes(string line, DcfDiagnostics diagnostics, int lineNo)
    {
        foreach (string key in new[] { "NAMESPACE=", "PATTERN=" })
        {
            int idx = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;
            int q1 = line.IndexOf('"', idx);
            if (q1 < 0)
            {
                diagnostics.Warn(DiagnosticCategory.Structure, $"malformed {key.TrimEnd('=')} (missing quotes)", lineNo);
                continue;
            }

            int q2 = line.IndexOf('"', q1 + 1);
            if (q2 < 0)
                diagnostics.Warn(DiagnosticCategory.Structure, $"malformed {key.TrimEnd('=')} (unclosed quotes)", lineNo);
        }
    }

    private static void ValidateSeqChoice(string value, DcfDiagnostics diagnostics, int lineNo, string field)
    {
        foreach (string key in new[] { "SEQ=", "CHOICE=" })
        {
            int idx = value.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx < 0)
                continue;
            int q1 = value.IndexOf('"', idx);
            if (q1 < 0)
            {
                diagnostics.Warn(DiagnosticCategory.FieldType, $"malformed {key.TrimEnd('=')}", lineNo, field);
                continue;
            }

            int q2 = value.IndexOf('"', q1 + 1);
            if (q2 < 0)
            {
                diagnostics.Warn(DiagnosticCategory.FieldType, $"unclosed {key.TrimEnd('=')}", lineNo, field);
                continue;
            }

            string inner = value[(q1 + 1)..q2];
            if (!int.TryParse(inner, out _))
                diagnostics.Warn(
                    DiagnosticCategory.FieldType,
                    $"{key.TrimEnd('=')} value '{inner}' is not an integer",
                    lineNo,
                    field
                );
        }
    }

    private static string StripMeta(string line)
    {
        foreach (string key in new[] { "PATTERN=", "NAMESPACE=" })
        {
            int idx = line.IndexOf(key, StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                line = line[..idx].TrimEnd();
        }

        return line;
    }

    private static string CurrentPath(Stack<string> stack)
    {
        if (stack.Count == 0)
            return Consts.DefaultBlockName;
        return Consts.DefaultBlockName + "\\" + string.Join("\\", stack.Reverse());
    }

    private static string GuessDocumentName(string text)
    {
        var m = Regex.Match(text, @"\[(?<name>[A-Za-z0-9_]+)\.(Header|Fields)\]", RegexOptions.IgnoreCase);
        return m.Success ? m.Groups["name"].Value : "Document";
    }

    private static int CountLinesBefore(string text, int index)
    {
        int count = 0;
        for (int i = 0; i < index && i < text.Length; i++)
        {
            if (text[i] == '\n')
                count++;
        }

        return count;
    }
}
