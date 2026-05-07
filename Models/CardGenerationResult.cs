namespace AnkiEnglishCardsBuilder.Models;

public sealed record CardGenerationResult(
    IReadOnlyList<AnkiCard> Cards,
    IReadOnlyList<string> Warnings);
