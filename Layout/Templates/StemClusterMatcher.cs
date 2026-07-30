using DcfToSf2.Parsing;

namespace DcfToSf2.Layout.Templates;

internal static class StemClusterMatcher
{
    public static FieldGroupMatch? MatchBySuffixes(
        IReadOnlyList<Data> fields,
        int startIndex,
        HashSet<string> ignoreFields,
        IReadOnlySet<string> suffixes,
        string templateName,
        int minCount = 2,
        Func<string, bool>? stemFilter = null,
        bool requireSameStem = true
    )
    {
        if (startIndex < 0 || startIndex >= fields.Count)
            return null;

        var first = fields[startIndex];
        if (LayoutUtil.IsBlockType(first) || LayoutUtil.IsBlockOpenMarker(first))
            return null;
        if (!LayoutUtil.IsVisible(first, ignoreFields))
            return null;

        string last = LayoutUtil.GetLastToken(first.FieldElement.ElementName);
        if (!suffixes.Contains(last))
            return null;

        string stem = LayoutUtil.GetStem(first.FieldElement.ElementName);
        if (stemFilter is not null && !stemFilter(stem))
            return null;

        var clustered = new List<Data>();
        Data? header = null;

        // Optional B header just before with same stem name
        if (startIndex > 0)
        {
            var prev = fields[startIndex - 1];
            if (
                LayoutUtil.IsBlockType(prev)
                && LayoutUtil.IsVisible(prev, ignoreFields)
                && (
                    prev.FieldElement.ElementName.Equals(stem, StringComparison.OrdinalIgnoreCase)
                    || LayoutUtil.GetLastToken(prev.FieldElement.ElementName).Equals(
                        LayoutUtil.GetLastToken(stem),
                        StringComparison.OrdinalIgnoreCase
                    )
                )
            )
            {
                header = prev;
            }
        }

        for (int i = startIndex; i < fields.Count; i++)
        {
            var f = fields[i];
            if (!LayoutUtil.IsVisible(f, ignoreFields))
                continue;
            if (LayoutUtil.IsBlockOpenMarker(f))
                break;
            if (LayoutUtil.IsBlockType(f))
            {
                if (clustered.Count > 0)
                    break;
                continue;
            }

            string fLast = LayoutUtil.GetLastToken(f.FieldElement.ElementName);
            string fStem = LayoutUtil.GetStem(f.FieldElement.ElementName);

            if (!suffixes.Contains(fLast))
            {
                if (clustered.Count > 0)
                    break;
                return null;
            }

            if (requireSameStem && !fStem.Equals(stem, StringComparison.OrdinalIgnoreCase))
            {
                if (clustered.Count > 0)
                    break;
                return null;
            }

            if (stemFilter is not null && !stemFilter(fStem))
            {
                if (clustered.Count > 0)
                    break;
                return null;
            }

            clustered.Add(f);
        }

        if (clustered.Count < minCount)
            return null;

        return new FieldGroupMatch
        {
            Fields = clustered,
            HeaderField = header,
            Stem = stem,
            TemplateName = templateName,
        };
    }

    public static List<Data> CollectByStemToken(
        IEnumerable<Data> fields,
        HashSet<string> ignoreFields,
        string stemToken,
        IReadOnlySet<string>? suffixes = null
    )
    {
        return fields
            .Where(f =>
            {
                if (!LayoutUtil.IsVisible(f, ignoreFields))
                    return false;
                if (LayoutUtil.IsBlockType(f) || LayoutUtil.IsBlockOpenMarker(f))
                    return false;
                string stem = LayoutUtil.GetStem(f.FieldElement.ElementName);
                if (!LayoutUtil.StemContains(stem, stemToken) && !stem.Equals(stemToken, StringComparison.OrdinalIgnoreCase))
                {
                    // also: Name is StemToken_Suffix
                    if (!f.FieldElement.ElementName.Contains(stemToken + "_", StringComparison.OrdinalIgnoreCase)
                        && !f.FieldElement.ElementName.StartsWith(stemToken + "_", StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                if (suffixes is null)
                    return true;
                return suffixes.Contains(LayoutUtil.GetLastToken(f.FieldElement.ElementName));
            })
            .ToList();
    }
}
