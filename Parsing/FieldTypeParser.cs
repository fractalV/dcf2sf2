using System.Text.RegularExpressions;

namespace DcfToSf2.Parsing;

internal static class FieldTypeParser
{
    // C50, N11.4, D10, B, L1, S1, 75x2, 90,90,70 250, C250 24
    private static readonly Regex TypeTokenRegex = new(
        @"^(?<tok>(?:[CBNDLS]\d+(?:\.\d+)?)|(?:\d+x\d+)|(?:\d+(?:,\d+)+)|B)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase
    );

    public static ParsedFieldType Parse(string elementValue)
    {
        string trimmed = elementValue.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return new ParsedFieldType { IsValid = true, RawToken = string.Empty, Kind = '\0' };
        }

        // Block type: "B |"
        if (trimmed.StartsWith("B ", StringComparison.OrdinalIgnoreCase) || trimmed.Equals("B", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("B|", StringComparison.OrdinalIgnoreCase))
        {
            return new ParsedFieldType
            {
                IsValid = true,
                IsBlock = true,
                Kind = 'B',
                RawToken = "B",
                Length = 0,
            };
        }

        var match = TypeTokenRegex.Match(trimmed);
        if (!match.Success)
        {
            // allow multi-size tokens like "90,90,70 250"
            int pipe = trimmed.IndexOf('|');
            string beforePipe = pipe >= 0 ? trimmed[..pipe].Trim() : trimmed;
            string first = beforePipe.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
            if (LooksLikeSizeGrid(first) || LooksLikeMultiRow(first))
            {
                return ParseSizeLike(first);
            }

            return new ParsedFieldType { IsValid = false, RawToken = first, Kind = '?' };
        }

        string tok = match.Groups["tok"].Value;
        if (tok.Contains('x', StringComparison.OrdinalIgnoreCase))
            return ParseSizeLike(tok);

        char kind = char.ToUpperInvariant(tok[0]);
        string digits = new(tok.Skip(1).TakeWhile(c => char.IsDigit(c) || c == '.').ToArray());
        int length = 16;
        if (digits.Contains('.'))
            digits = digits[..digits.IndexOf('.')];
        if (int.TryParse(digits, out int parsedLen) && parsedLen > 0)
            length = parsedLen;

        int rows = 1;
        // "75x2" already handled; "C250 24" second number is display width hint
        return new ParsedFieldType
        {
            IsValid = true,
            Kind = kind,
            RawToken = tok,
            Length = length,
            Rows = rows,
        };
    }

    private static bool LooksLikeSizeGrid(string token) =>
        token.Contains(',') && token.Split(',').All(p => int.TryParse(p, out _));

    private static bool LooksLikeMultiRow(string token) =>
        token.Contains('x', StringComparison.OrdinalIgnoreCase)
        && token.Split('x', StringSplitOptions.RemoveEmptyEntries).Length == 2;

    private static ParsedFieldType ParseSizeLike(string token)
    {
        int rows = 1;
        int length = 150;
        if (token.Contains('x', StringComparison.OrdinalIgnoreCase))
        {
            var parts = token.Split('x', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], out int w) && int.TryParse(parts[1], out int h))
            {
                length = w;
                rows = Math.Max(1, h);
            }
        }
        else if (token.Contains(','))
        {
            var parts = token.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && int.TryParse(parts[0], out int w))
                length = w;
            rows = Math.Max(1, parts.Length);
        }

        return new ParsedFieldType
        {
            IsValid = true,
            Kind = 'M',
            RawToken = token,
            Length = length,
            Rows = rows,
        };
    }

    public static int DisplayWidth(ParsedFieldType type)
    {
        if (!type.IsValid || type.IsBlock)
            return 16;

        int width = type.Length * Consts.DefaultWidthFidChar;
        if (width < 16)
            width = 16;
        if (width > Consts.DefaultWidthBlock)
            width = Consts.DefaultWidthBlock;
        return width;
    }
}
