namespace AnkiEnglishCardsBuilder.Models;

public sealed class AppSettings
{
    public int SettingsVersion { get; set; } = 2;

    public string Provider { get; set; } = "OpenAI";

    public OpenAiSettings OpenAI { get; set; } = new();
}

public sealed class OpenAiSettings
{
    public string ApiKey { get; set; } = string.Empty;

    public string Model { get; set; } = "gpt-5-mini";

    public int TimeoutSeconds { get; set; } = 450;

    public int BatchSize { get; set; } = 10;
}
