using System.Text;
using AnkiEnglishCardsBuilder.Models;

namespace AnkiEnglishCardsBuilder.Services;

public sealed class AnkiTsvExporter
{
    public async Task ExportAsync(IEnumerable<AnkiCard> cards, string path, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        builder.AppendLine("#separator:tab");
        builder.AppendLine("#html:true");
        builder.AppendLine("#tags:english vocabulary");

        foreach (var card in cards)
        {
            var fields = new[]
            {
                BuildFront(card),
                BuildBack(card)
            };

            builder.AppendLine(string.Join('\t', fields.Select(CleanField)));
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    private static string CleanField(string value)
    {
        var normalized = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Replace("\t", " ", StringComparison.Ordinal)
            .Trim();

        return normalized.Replace("\n", "<br>", StringComparison.Ordinal);
    }

    private static string BuildFront(AnkiCard card)
    {
        var sections = new List<string>();

        if (!string.IsNullOrWhiteSpace(card.Word))
        {
            sections.Add($"<b>{EscapeHtml(card.Word)}</b>");
        }

        foreach (var example in SplitLines(card.ExampleSentence))
        {
            sections.Add($"<span>{EscapeHtml(example)}</span>");
        }

        return string.Join("<br>", sections);
    }

    private static string BuildBack(AnkiCard card)
    {
        var sections = new List<string>();

        AddSection(sections, "Meaning", card.ItalianMeaning);
        AddSection(sections, "Definition", card.EnglishDefinition);
        AddSection(sections, "Part of speech", card.PartOfSpeech);
        AddSection(sections, "Translations", card.ExampleTranslation);
        AddSection(sections, "Usage notes", card.UsageNotes);
        AddSection(sections, "Synonyms", card.Synonyms);

        return string.Join("<br><br>", sections);
    }

    private static void AddSection(List<string> sections, string label, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        sections.Add($"<b>{EscapeHtml(label)}:</b> {EscapeHtml(value)}");
    }

    private static IEnumerable<string> SplitLines(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    private static string EscapeHtml(string value)
    {
        return value
            .Replace("&", "&amp;", StringComparison.Ordinal)
            .Replace("<", "&lt;", StringComparison.Ordinal)
            .Replace(">", "&gt;", StringComparison.Ordinal)
            .Replace("\"", "&quot;", StringComparison.Ordinal);
    }
}
