using System.Net.Http;
using AnkiEnglishCardsBuilder.Models;

namespace AnkiEnglishCardsBuilder.Services;

public sealed class CardEnrichmentProviderFactory
{
    public ICardEnrichmentProvider Create(AppSettings settings)
    {
        return settings.Provider switch
        {
            "OpenAI" => new OpenAiCardEnrichmentProvider(settings.OpenAI, new HttpClient()),
            _ => throw new NotSupportedException($"Provider '{settings.Provider}' is not supported.")
        };
    }
}
