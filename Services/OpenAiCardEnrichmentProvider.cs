using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AnkiEnglishCardsBuilder.Models;

namespace AnkiEnglishCardsBuilder.Services;

public sealed class OpenAiCardEnrichmentProvider(OpenAiSettings settings, HttpClient httpClient) : ICardEnrichmentProvider
{
    private const string Endpoint = "https://api.openai.com/v1/responses";

    public string Name => "OpenAI";

    public async Task<CardGenerationResult> EnrichAsync(
        IReadOnlyList<string> words,
        IProgress<ProgressReport> progress,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            throw new InvalidOperationException("Chiave OpenAI mancante. Apri Settings e inserisci la API key.");
        }

        if (words.Count == 0)
        {
            return new CardGenerationResult([], []);
        }

        var cards = new List<AnkiCard>();
        var warnings = new List<string>();
        var batchSize = Math.Clamp(settings.BatchSize, 1, 25);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 900)));

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);

        for (var index = 0; index < words.Count; index += batchSize)
        {
            var batch = words.Skip(index).Take(batchSize).ToArray();
            progress.Report(new ProgressReport(
                $"Genero card {index + 1}-{index + batch.Length} di {words.Count} con {settings.Model}...",
                index,
                words.Count));

            var result = await SendBatchWithRetriesAsync(batch, timeoutCts.Token);
            cards.AddRange(result.Cards);
            warnings.AddRange(result.Warnings);
        }

        return new CardGenerationResult(cards, warnings);
    }

    private async Task<CardGenerationResult> SendBatchWithRetriesAsync(IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await SendBatchAsync(words, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsTransient(ex.StatusCode))
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < maxAttempts)
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }

        return await SendBatchAsync(words, cancellationToken);
    }

    private async Task<CardGenerationResult> SendBatchAsync(IReadOnlyList<string> words, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Endpoint);
        request.Content = new StringContent(BuildRequestJson(words), Encoding.UTF8, "application/json");

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw BuildOpenAiException(response.StatusCode, body);
        }

        return ParseResponse(body, words);
    }

    private string BuildRequestJson(IReadOnlyList<string> words)
    {
        var payload = new JsonObject
        {
            ["model"] = settings.Model,
            ["instructions"] = """
                You create Anki cards for an Italian speaker learning English.
                Return concise, natural, learner-friendly content.
                Use simple English definitions. Do not invent rare meanings unless the word clearly requires them.
                """,
            ["input"] = $"""
                Create one card for each English word below.
                Words: {JsonSerializer.Serialize(words)}

                Requirements:
                - Keep exampleSentence short and natural.
                - exampleTranslation must be Italian.
                - If a word has an obvious typo or spelling mistake, correct it in the card's word field.
                - When you correct a typo, mention the original input and the correction in usageNotes and add a short warning.
                - If the item is a phrasal verb, explicitly say it in usageNotes.
                - In usageNotes, add grammar/pattern details when relevant: required prepositions, particles,
                  whether it is followed by a gerund (-ing), infinitive, noun, object, or a specific construction.
                  Example: "Phrasal verb. 'Be used to' is followed by a gerund (-ing) or by a noun."
                - Always evaluate whether the word has useful synonyms for an English learner.
                - synonyms must contain 0 to 4 common English synonyms as a comma-separated string; leave it empty only when no useful synonym exists.
                - tags should be lowercase, space-separated Anki tags.
                """,
            ["text"] = new JsonObject
            {
                ["format"] = BuildJsonSchema()
            }
        };

        return payload.ToJsonString();
    }

    private static JsonObject BuildJsonSchema()
    {
        var cardSchema = new JsonObject
        {
            ["type"] = "object",
            ["additionalProperties"] = false,
            ["properties"] = new JsonObject
            {
                ["word"] = StringSchema(),
                ["partOfSpeech"] = StringSchema(),
                ["italianMeaning"] = StringSchema(),
                ["englishDefinition"] = StringSchema(),
                ["exampleSentence"] = StringSchema(),
                ["exampleTranslation"] = StringSchema(),
                ["usageNotes"] = StringSchema(),
                ["cefrLevel"] = StringSchema(),
                ["synonyms"] = StringSchema(),
                ["tags"] = StringSchema()
            },
            ["required"] = new JsonArray
            {
                "word", "partOfSpeech", "italianMeaning", "englishDefinition",
                "exampleSentence", "exampleTranslation", "usageNotes", "cefrLevel", "synonyms", "tags"
            }
        };

        return new JsonObject
        {
            ["type"] = "json_schema",
            ["name"] = "anki_cards_batch",
            ["strict"] = true,
            ["schema"] = new JsonObject
            {
                ["type"] = "object",
                ["additionalProperties"] = false,
                ["properties"] = new JsonObject
                {
                    ["cards"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = cardSchema
                    },
                    ["warnings"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["items"] = StringSchema()
                    }
                },
                ["required"] = new JsonArray { "cards", "warnings" }
            }
        };
    }

    private static JsonObject StringSchema() => new() { ["type"] = "string" };

    private static CardGenerationResult ParseResponse(string responseBody, IReadOnlyList<string> requestedWords)
    {
        using var document = JsonDocument.Parse(responseBody);
        var root = document.RootElement;
        var outputText = ExtractOutputText(root);

        if (string.IsNullOrWhiteSpace(outputText))
        {
            throw new InvalidOperationException("OpenAI ha risposto senza testo utilizzabile. Riprova o cambia modello.");
        }

        using var content = JsonDocument.Parse(outputText);
        var cards = new List<AnkiCard>();
        var warnings = new List<string>();

        if (content.RootElement.TryGetProperty("cards", out var cardsElement))
        {
            foreach (var item in cardsElement.EnumerateArray())
            {
                cards.Add(new AnkiCard
                {
                    Word = ReadString(item, "word"),
                    PartOfSpeech = ReadString(item, "partOfSpeech"),
                    ItalianMeaning = ReadString(item, "italianMeaning"),
                    EnglishDefinition = ReadString(item, "englishDefinition"),
                    ExampleSentence = ReadString(item, "exampleSentence"),
                    ExampleTranslation = ReadString(item, "exampleTranslation"),
                    UsageNotes = ReadString(item, "usageNotes"),
                    CefrLevel = ReadString(item, "cefrLevel"),
                    Synonyms = ReadString(item, "synonyms"),
                    Tags = ReadString(item, "tags"),
                    Status = "Generated"
                });
            }
        }

        if (content.RootElement.TryGetProperty("warnings", out var warningsElement))
        {
            warnings.AddRange(warningsElement.EnumerateArray().Select(warning => warning.GetString() ?? string.Empty));
        }

        var missing = requestedWords
            .Where(word => cards.All(card => !string.Equals(card.Word, word, StringComparison.OrdinalIgnoreCase)))
            .ToArray();

        foreach (var word in missing)
        {
            cards.Add(new AnkiCard
            {
                Word = word,
                Status = "Needs review",
                Error = "OpenAI non ha restituito contenuti per questa parola."
            });
        }

        return new CardGenerationResult(cards, warnings);
    }

    private static string ExtractOutputText(JsonElement root)
    {
        if (root.TryGetProperty("output_text", out var outputText))
        {
            return outputText.GetString() ?? string.Empty;
        }

        if (!root.TryGetProperty("output", out var output))
        {
            return string.Empty;
        }

        foreach (var item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("content", out var content))
            {
                continue;
            }

            foreach (var contentItem in content.EnumerateArray())
            {
                if (contentItem.TryGetProperty("type", out var type)
                    && type.GetString() == "output_text"
                    && contentItem.TryGetProperty("text", out var text))
                {
                    return text.GetString() ?? string.Empty;
                }
            }
        }

        return string.Empty;
    }

    private static string ReadString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property)
            ? property.GetString() ?? string.Empty
            : string.Empty;
    }

    private static Exception BuildOpenAiException(HttpStatusCode statusCode, string body)
    {
        var message = statusCode switch
        {
            HttpStatusCode.Unauthorized => "OpenAI ha rifiutato la chiave API. Controlla la chiave nei Settings.",
            HttpStatusCode.TooManyRequests => "OpenAI segnala rate limit o quota esaurita. Attendi qualche minuto o cambia progetto/modello.",
            HttpStatusCode.BadRequest => "La richiesta a OpenAI non è valida. Il modello selezionato potrebbe non supportare l'output strutturato.",
            HttpStatusCode.RequestTimeout => "Timeout verso OpenAI. Controlla la rete o aumenta il timeout nei Settings.",
            _ => $"Errore OpenAI {(int)statusCode}: {statusCode}."
        };

        return new InvalidOperationException($"{message}\n\nDettagli: {TrimBody(body)}");
    }

    private static bool IsTransient(HttpStatusCode? statusCode)
    {
        return statusCode is null
               || statusCode == HttpStatusCode.RequestTimeout
               || statusCode == HttpStatusCode.TooManyRequests
               || (int)statusCode >= 500;
    }

    private static string TrimBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "Nessun dettaglio restituito dal server.";
        }

        return body.Length <= 800 ? body : string.Concat(body.AsSpan(0, 800), "...");
    }
}
