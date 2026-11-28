namespace DcfToSf2.Layout.Templates;

internal sealed class FieldGroupTemplateRegistry
{
    private readonly List<IFieldGroupTemplate> _builtIn;
    private readonly List<UserDefinedFieldGroupTemplate> _user = [];
    private readonly OrganizationFieldGroupTemplate _organization = new();
    private readonly PrecedingDocDetailsFieldGroupTemplate _precedingDoc = new();
    private readonly HashSet<string> _builtInNames;

    public FieldGroupTemplateRegistry()
    {
        _builtIn =
        [
            _precedingDoc,
            new IdentityCardFieldGroupTemplate(),
            new ContactFieldGroupTemplate(),
            new AddressFieldGroupTemplate(),
            new CustomsDocIdFieldGroupTemplate(),
            new PiDocumentNumberFieldGroupTemplate(),
            new TirIdFieldGroupTemplate(),
            new GoodsQuantityFieldGroupTemplate(),
        ];
        _builtInNames = new HashSet<string>(
            _builtIn.Select(t => t.Name).Append(_organization.Name),
            StringComparer.OrdinalIgnoreCase
        );
    }

    public static FieldGroupTemplateRegistry Default { get; } = new();

    public OrganizationFieldGroupTemplate Organization => _organization;

    public IReadOnlyList<IFieldGroupTemplate> All
    {
        get
        {
            var list = new List<IFieldGroupTemplate> { _organization };
            list.AddRange(_builtIn);
            list.AddRange(_user);
            return list;
        }
    }

    public IReadOnlyList<UserDefinedFieldGroupTemplate> UserTemplates => _user;

    public bool IsBuiltInName(string name) => _builtInNames.Contains(name);

    public bool IsUserTemplate(string name) =>
        _user.Any(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public void ReloadUserTemplates(IEnumerable<UserTemplateDefinitionDto> definitions)
    {
        _user.Clear();
        foreach (var def in definitions)
        {
            if (string.IsNullOrWhiteSpace(def.Name) || def.Fields is null || def.Fields.Count == 0)
                continue;
            if (_builtInNames.Contains(def.Name))
                continue;
            _user.Add(new UserDefinedFieldGroupTemplate(def));
        }
    }

    public (IFieldGroupTemplate Template, FieldGroupMatch Match)? TryMatchAt(
        IReadOnlyList<Data> fields,
        int startIndex,
        HashSet<string> ignoreFields,
        string? blockPath = null
    )
    {
        if (
            startIndex == 0
            && PrecedingDocDetailsFieldGroupTemplate.MatchesPath(blockPath)
        )
        {
            var m = _precedingDoc.TryMatch(fields, 0, ignoreFields);
            if (m is not null)
                return (_precedingDoc, m);
        }

        if (startIndex == 0)
        {
            foreach (var ut in _user)
            {
                var pathMatch = ut.TryMatchPath(fields, ignoreFields, blockPath);
                if (pathMatch is not null)
                    return (ut, pathMatch);
            }
        }

        foreach (var t in _builtIn)
        {
            var match = t.TryMatch(fields, startIndex, ignoreFields);
            if (match is not null)
                return (t, match);
        }

        foreach (var ut in _user)
        {
            var match = ut.TryMatch(fields, startIndex, ignoreFields);
            if (match is not null)
                return (ut, match);
        }

        return null;
    }

    public int LayoutWithTemplates(
        Block block,
        List<Data> fields,
        int baseX,
        int startY,
        int maxWidth,
        HashSet<string> ignoreFields,
        string? blockPath,
        Func<Block, List<Data>, int, int, int, int>? stackFallback
    )
    {
        int y = startY;
        var remaining = fields
            .Where(f => LayoutUtil.IsVisible(f, ignoreFields) && !LayoutUtil.IsBlockOpenMarker(f))
            .ToList();

        if (PrecedingDocDetailsFieldGroupTemplate.MatchesPath(blockPath))
        {
            var m = _precedingDoc.TryMatch(remaining, 0, ignoreFields);
            if (m is not null)
            {
                y = _precedingDoc.Layout(block, m, baseX, y, maxWidth, ignoreFields);
                var used = new HashSet<string>(m.Fields.Select(f => f.FieldElement.ElementName), StringComparer.OrdinalIgnoreCase);
                if (m.HeaderField is not null)
                    used.Add(m.HeaderField.FieldElement.ElementName);
                remaining = remaining.Where(f => !used.Contains(f.FieldElement.ElementName)).ToList();
            }
        }

        foreach (var ut in _user)
        {
            var pathMatch = ut.TryMatchPath(remaining, ignoreFields, blockPath);
            if (pathMatch is null)
                continue;
            y = ut.Layout(block, pathMatch, baseX, y, maxWidth, ignoreFields);
            var used = new HashSet<string>(
                pathMatch.Fields.Select(f => f.FieldElement.ElementName),
                StringComparer.OrdinalIgnoreCase
            );
            remaining = remaining.Where(f => !used.Contains(f.FieldElement.ElementName)).ToList();
            break;
        }

        var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int i = 0;
        while (i < remaining.Count)
        {
            if (usedNames.Contains(remaining[i].FieldElement.ElementName))
            {
                i++;
                continue;
            }

            if (LayoutUtil.IsBlockType(remaining[i]))
            {
                i++;
                continue;
            }

            var hit = TryMatchAt(remaining, i, ignoreFields, blockPath);
            if (hit is not null)
            {
                var (template, match) = hit.Value;
                if (
                    template.Name == PrecedingDocDetailsFieldGroupTemplate.TemplateName
                    && PrecedingDocDetailsFieldGroupTemplate.MatchesPath(blockPath)
                    && i > 0
                )
                {
                    i++;
                    continue;
                }

                if (template is UserDefinedFieldGroupTemplate && i == 0
                    && UserDefinedFieldGroupTemplate.MatchesPath(blockPath, ((UserDefinedFieldGroupTemplate)template).MatchPath))
                {
                    i++;
                    continue;
                }

                y = template.Layout(block, match, baseX, y, maxWidth, ignoreFields);
                foreach (var f in match.Fields)
                    usedNames.Add(f.FieldElement.ElementName);
                if (match.HeaderField is not null)
                    usedNames.Add(match.HeaderField.FieldElement.ElementName);
                i++;
                continue;
            }

            var batch = new List<Data>();
            while (i < remaining.Count)
            {
                var f = remaining[i];
                if (usedNames.Contains(f.FieldElement.ElementName) || LayoutUtil.IsBlockType(f))
                {
                    i++;
                    continue;
                }

                if (TryMatchAt(remaining, i, ignoreFields, blockPath) is not null)
                    break;

                batch.Add(f);
                usedNames.Add(f.FieldElement.ElementName);
                i++;
            }

            if (batch.Count > 0 && stackFallback is not null)
                y = stackFallback(block, batch, baseX, y, maxWidth);
        }

        return y;
    }
}
