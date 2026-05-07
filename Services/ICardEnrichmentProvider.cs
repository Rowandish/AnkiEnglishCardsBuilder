using AnkiEnglishCardsBuilder.Models;

namespace AnkiEnglishCardsBuilder.Services;

public interface ICardEnrichmentProvider
{
    string Name { get; }

    Task<CardGenerationResult> EnrichAsync(
        IReadOnlyList<string> words,
        IProgress<ProgressReport> progress,
        CancellationToken cancellationToken);
}
