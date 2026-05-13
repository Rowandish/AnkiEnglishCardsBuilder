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
            throw new InvalidOperationException("Missing OpenAI API key. Open Settings and enter the API key.");
        }

        if (words.Count == 0)
        {
            return new CardGenerationResult([], []);
        }

        var cards = new List<AnkiCard>();
        var warnings = new List<string>();
        var batchSize = Math.Clamp(settings.BatchSize, 1, 25);
        var requestTimeout = TimeSpan.FromSeconds(Math.Clamp(settings.TimeoutSeconds, 10, 900));

        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        httpClient.Timeout = Timeout.InfiniteTimeSpan;

        for (var index = 0; index < words.Count; index += batchSize)
        {
            var batch = words.Skip(index).Take(batchSize).ToArray();
            progress.Report(new ProgressReport(
                $"Generating cards {index + 1}-{index + batch.Length} of {words.Count} with {settings.Model}...",
                index,
                words.Count));

            var result = await SendBatchWithRetriesAsync(batch, requestTimeout, cancellationToken);
            cards.AddRange(result.Cards);
            warnings.AddRange(result.Warnings);
        }

        return new CardGenerationResult(cards, warnings);
    }

    private async Task<CardGenerationResult> SendBatchWithRetriesAsync(
        IReadOnlyList<string> words,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                return await SendBatchWithTimeoutAsync(words, requestTimeout, cancellationToken);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts && IsTransient(ex.StatusCode))
            {
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
            catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
            {
                if (attempt >= maxAttempts)
                {
                    throw BuildRequestTimeoutException(requestTimeout, ex);
                }

                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken);
            }
        }

        throw new InvalidOperationException("The OpenAI request was not completed.");
    }

    private async Task<CardGenerationResult> SendBatchWithTimeoutAsync(
        IReadOnlyList<string> words,
        TimeSpan requestTimeout,
        CancellationToken cancellationToken)
    {
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(requestTimeout);

        return await SendBatchAsync(words, requestCts.Token);
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
                - Return exactly two English examples per card: exampleSentence1 and exampleSentence2.
                - Keep each example short, natural, and useful on the front of an Anki card.
                - If the word has two clearly different common meanings, use one example for each meaning.
                - If the word has one main meaning or no useful ambiguity, use two different examples for that same meaning.
                - If the word has more than two common meanings, cover the two most useful meanings for an English learner.
                - exampleTranslation1 and exampleTranslation2 must be Italian and must match the two examples.
                - Do not number the example fields; the app will number them in the card preview/export.
                - Make italianMeaning and englishDefinition cover the meaning or meanings used in the examples.
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
                ["exampleSentence1"] = StringSchema(),
                ["exampleSentence2"] = StringSchema(),
                ["exampleTranslation1"] = StringSchema(),
                ["exampleTranslation2"] = StringSchema(),
                ["usageNotes"] = StringSchema(),
                ["cefrLevel"] = StringSchema(),
                ["synonyms"] = StringSchema(),
                ["tags"] = StringSchema()
            },
            ["required"] = new JsonArray
            {
                "word", "partOfSpeech", "italianMeaning", "englishDefinition",
                "exampleSentence1", "exampleSentence2", "exampleTranslation1", "exampleTranslation2",
                "usageNotes", "cefrLevel", "synonyms", "tags"
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
            throw new InvalidOperationException("OpenAI returned no usable text. Try again or change model.");
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
                    ExampleSentence = BuildNumberedLines(
                        ReadString(item, "exampleSentence1"),
                        ReadString(item, "exampleSentence2")),
                    ExampleTranslation = BuildNumberedLines(
                        ReadString(item, "exampleTranslation1"),
                        ReadString(item, "exampleTranslation2")),
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
                Error = "OpenAI did not return content for this word."
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

    private static string BuildNumberedLines(params string[] values)
    {
        var lines = values
            .Select(value => value.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select((value, index) => $"{index + 1}. {value}");

        return string.Join(Environment.NewLine, lines);
    }

    private static Exception BuildOpenAiException(HttpStatusCode statusCode, string body)
    {
        var message = statusCode switch
        {
            HttpStatusCode.Unauthorized => "OpenAI rejected the API key. Check the key in Settings.",
            HttpStatusCode.TooManyRequests => "OpenAI reported a rate limit or exhausted quota. Wait a few minutes or change project/model.",
            HttpStatusCode.BadRequest => "The OpenAI request is invalid. The selected model may not support structured output.",
            HttpStatusCode.RequestTimeout => "OpenAI request timeout. Check the network or increase the timeout in Settings.",
            _ => $"OpenAI error {(int)statusCode}: {statusCode}."
        };

        return new InvalidOperationException($"{message}\n\nDettagli: {TrimBody(body)}");
    }

    private static TimeoutException BuildRequestTimeoutException(TimeSpan requestTimeout, Exception innerException)
    {
        return new TimeoutException(
            $"OpenAI timed out after {requestTimeout.TotalSeconds:0} seconds for a single request.",
            innerException);
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
            return "No details returned by the server.";
        }

        return body.Length <= 800 ? body : string.Concat(body.AsSpan(0, 800), "...");
    }
}
