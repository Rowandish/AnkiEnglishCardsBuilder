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
                card.Word,
                card.PartOfSpeech,
                card.ItalianMeaning,
                card.EnglishDefinition,
                card.ExampleSentence,
                card.ExampleTranslation,
                card.UsageNotes,
                card.CefrLevel,
                card.Synonyms,
                card.Tags
            };

            builder.AppendLine(string.Join('\t', fields.Select(CleanField)));
        }

        await File.WriteAllTextAsync(path, builder.ToString(), new UTF8Encoding(false), cancellationToken);
    }

    private static string CleanField(string value)
    {
        return value
            .Replace("\t", " ", StringComparison.Ordinal)
            .Replace("\r\n", "<br>", StringComparison.Ordinal)
            .Replace('\n', ' ')
            .Replace('\r', ' ')
            .Trim();
    }
}
