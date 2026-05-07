using System.Collections.ObjectModel;
using AnkiEnglishCardsBuilder.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AnkiEnglishCardsBuilder.ViewModels;

public sealed partial class SettingsWindowViewModel : ViewModelBase
{
    [ObservableProperty]
    private string provider = "OpenAI";

    [ObservableProperty]
    private string openAiApiKey = string.Empty;

    [ObservableProperty]
    private string openAiModel = "gpt-5-mini";

    [ObservableProperty]
    private int timeoutSeconds = 450;

    [ObservableProperty]
    private int batchSize = 10;

    public ObservableCollection<string> Providers { get; } = ["OpenAI"];

    public ObservableCollection<string> OpenAiModels { get; } =
    [
        "gpt-5-mini",
        "gpt-5",
        "gpt-4.1-mini",
        "gpt-4.1",
        "gpt-4o-mini"
    ];

    public static SettingsWindowViewModel FromSettings(AppSettings settings)
    {
        return new SettingsWindowViewModel
        {
            Provider = settings.Provider,
            OpenAiApiKey = settings.OpenAI.ApiKey,
            OpenAiModel = settings.OpenAI.Model,
            TimeoutSeconds = settings.OpenAI.TimeoutSeconds,
            BatchSize = settings.OpenAI.BatchSize
        };
    }

    public AppSettings ToSettings()
    {
        return new AppSettings
        {
            SettingsVersion = 2,
            Provider = Provider,
            OpenAI = new OpenAiSettings
            {
                ApiKey = OpenAiApiKey.Trim(),
                Model = OpenAiModel.Trim(),
                TimeoutSeconds = Math.Clamp(TimeoutSeconds, 10, 900),
                BatchSize = Math.Clamp(BatchSize, 1, 25)
            }
        };
    }
}
