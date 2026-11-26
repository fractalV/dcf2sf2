using DcfToSf2.Layout;
using DcfToSf2.Parsing;

namespace DcfToSf2.Ui;

internal static class IgnoreFieldsTreeBuilder
{
    public static List<IgnoreBlockNode> Build(DcfParseResult parsed, HashSet<string> manualChecked)
    {
        var byPath = new Dictionary<string, List<Data>>(StringComparer.OrdinalIgnoreCase);

        foreach (var f in parsed.Fields)
        {
            if (LayoutUtil.IsBlockOpenMarker(f))
                continue;
            if (DcfParser.IsBuiltInServiceField(f.FieldElement.ElementName))
                continue;

            string path = string.IsNullOrWhiteSpace(f.Path) ? Consts.DefaultBlockName : f.Path;
            if (!byPath.TryGetValue(path, out var list))
            {
                list = [];
                byPath[path] = list;
            }

            list.Add(f);
        }

        var leafNodes = new Dictionary<string, IgnoreBlockNode>(StringComparer.OrdinalIgnoreCase);
        foreach (var (path, fields) in byPath)
        {
            string title =
                fields.Select(f => f.RusNameBlock).FirstOrDefault(s => !string.IsNullOrWhiteSpace(s))
                ?? IgnoreBlockNode.LeafName(path);

            leafNodes[path] = BuildPathNode(path, title, fields, manualChecked);
        }

        foreach (var path in leafNodes.Keys.ToList())
        {
            string current = path;
            while (true)
            {
                int idx = current.LastIndexOf('\\');
                if (idx <= 0)
                    break;

                string parentPath = current[..idx];
                if (!leafNodes.ContainsKey(parentPath))
                {
                    leafNodes[parentPath] = new IgnoreBlockNode(
                        parentPath,
                        IgnoreBlockNode.LeafName(parentPath),
                        []
                    );
                }

                current = parentPath;
            }
        }

        var roots = new List<IgnoreBlockNode>();
        foreach (
            var path in leafNodes.Keys
                .OrderBy(p => p.Count(c => c == '\\'))
                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
        )
        {
            var node = leafNodes[path];
            int idx = path.LastIndexOf('\\');
            if (idx <= 0)
            {
                roots.Add(node);
                continue;
            }

            string parentPath = path[..idx];
            if (leafNodes.TryGetValue(parentPath, out var parent))
            {
                parent.ChildBlocks.Add(node);
                node.ParentBlock = parent;
            }
            else
            {
                roots.Add(node);
            }
        }

        foreach (var root in roots)
            root.RefreshCheckState();

        return roots;
    }

    private static IgnoreBlockNode BuildPathNode(
        string path,
        string title,
        List<Data> fields,
        HashSet<string> manualChecked
    )
    {
        var ordered = fields.OrderBy(f => f.SourceLine).ThenBy(f => f.FieldElement.ElementName).ToList();
        var sectionHeaders = ordered.Where(LayoutUtil.IsBlockType).ToList();

        if (sectionHeaders.Count == 0)
        {
            var flat = ordered.Select(f => ToFieldNode(f, manualChecked)).ToList();
            return new IgnoreBlockNode(path, title, flat);
        }

        var sectionKeys = new List<string>();
        var sections = new Dictionary<string, IgnoreBlockNode>(StringComparer.OrdinalIgnoreCase);

        foreach (var header in sectionHeaders)
        {
            string key = header.FieldElement.ElementName;
            if (sections.ContainsKey(key))
                continue;

            string label = LayoutUtil.ExtractComment(header.FieldElement.ElementValue);
            if (string.IsNullOrWhiteSpace(label))
                label = key;

            var section = new IgnoreBlockNode(path, label, [], sectionKey: key);
            sections[key] = section;
            sectionKeys.Add(key);

            string? ns = ExtractNamespace(header.FieldElement.ElementValue);
            if (
                !string.IsNullOrWhiteSpace(ns)
                && !ns.Equals(key, StringComparison.OrdinalIgnoreCase)
                && !sections.ContainsKey(ns)
            )
            {
                sections[ns] = section;
            }
        }

        var direct = new List<IgnoreFieldNode>();
        foreach (var f in ordered)
        {
            if (LayoutUtil.IsBlockType(f))
                continue;

            string name = f.FieldElement.ElementName;
            string? stem = MatchSectionStem(name, f.FieldElement.ElementValue, sectionKeys, sections.Keys);
            if (stem is not null && sections.TryGetValue(stem, out var section))
            {
                var node = ToFieldNode(f, manualChecked);
                node.Parent = section;
                section.Fields.Add(node);
            }
            else
            {
                direct.Add(ToFieldNode(f, manualChecked));
            }
        }


        var childSections = new List<IgnoreBlockNode>();
        var seen = new HashSet<IgnoreBlockNode>();
        foreach (var key in sectionKeys)
        {
            var section = sections[key];
            if (!seen.Add(section))
                continue;

            childSections.Add(section);
        }

        return new IgnoreBlockNode(path, title, direct, childSections);
    }

    private static IgnoreFieldNode ToFieldNode(Data f, HashSet<string> manualChecked)
    {
        string name = f.FieldElement.ElementName;
        string label = LayoutUtil.ExtractComment(f.FieldElement.ElementValue);
        if (string.IsNullOrWhiteSpace(label))
            label = DcfParser.GetLastToken(name);

        return new IgnoreFieldNode(name, label, manualChecked.Contains(name));
    }

    private static string? MatchSectionStem(
        string fieldName,
        string elementValue,
        IReadOnlyList<string> sectionKeys,
        IEnumerable<string> allAliases
    )
    {
        string? fieldNs = ExtractNamespace(elementValue);
        if (!string.IsNullOrWhiteSpace(fieldNs))
        {
            foreach (var alias in allAliases.OrderByDescending(s => s.Length))
            {
                if (fieldNs.Equals(alias, StringComparison.OrdinalIgnoreCase))
                    return alias;
            }
        }

        foreach (var sec in sectionKeys.OrderByDescending(s => s.Length))
        {
            if (
                fieldName.Equals(sec, StringComparison.OrdinalIgnoreCase)
                || fieldName.StartsWith(sec + "_", StringComparison.OrdinalIgnoreCase)
            )
                return sec;
        }

        return null;
    }

    internal static string? ExtractNamespace(string elementValue)
    {
        const string key = "NAMESPACE=";
        int idx = elementValue.IndexOf(key, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return null;

        int q1 = elementValue.IndexOf('"', idx + key.Length);
        if (q1 < 0)
            return null;
        int q2 = elementValue.IndexOf('"', q1 + 1);
        if (q2 < 0)
            return null;

        string ns = elementValue[(q1 + 1)..q2].Trim();
        return string.IsNullOrEmpty(ns) ? null : ns;
    }
}
