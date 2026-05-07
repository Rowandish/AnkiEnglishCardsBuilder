using System.Text.RegularExpressions;

namespace AnkiEnglishCardsBuilder.Services;

public sealed class WordParser
{
    private static readonly Regex SeparatorRegex = new(@"[,;\r\n]+", RegexOptions.Compiled);

    public IReadOnlyList<string> Parse(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return [];
        }

        return SeparatorRegex
            .Split(input)
            .Select(word => word.Trim())
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(word => word, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }
}
