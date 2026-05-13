using System.Text.RegularExpressions;

namespace AnkiEnglishCardsBuilder.Services;

public sealed class WordParser
{
    private static readonly Regex SeparatorRegex = new(@"[,;\r\n]+", RegexOptions.Compiled);

    public IReadOnlyList<string> Parse(string input)
    {
        return ParseWithStats(input).Words;
    }

    public WordParseResult ParseWithStats(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return new WordParseResult([], 0);
        }

        var parsedWords = SeparatorRegex
            .Split(input)
            .Select(word => word.Trim())
            .Where(word => !string.IsNullOrWhiteSpace(word))
            .ToArray();

        var uniqueWords = parsedWords
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(word => word, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new WordParseResult(uniqueWords, parsedWords.Length - uniqueWords.Length);
    }
}

public sealed record WordParseResult(IReadOnlyList<string> Words, int DuplicatesRemoved);
