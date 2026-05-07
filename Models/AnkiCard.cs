using CommunityToolkit.Mvvm.ComponentModel;

namespace AnkiEnglishCardsBuilder.Models;

public sealed partial class AnkiCard : ObservableObject
{
    [ObservableProperty]
    private string word = string.Empty;

    [ObservableProperty]
    private string partOfSpeech = string.Empty;

    [ObservableProperty]
    private string italianMeaning = string.Empty;

    [ObservableProperty]
    private string englishDefinition = string.Empty;

    [ObservableProperty]
    private string exampleSentence = string.Empty;

    [ObservableProperty]
    private string exampleTranslation = string.Empty;

    [ObservableProperty]
    private string usageNotes = string.Empty;

    [ObservableProperty]
    private string cefrLevel = string.Empty;

    [ObservableProperty]
    private string synonyms = string.Empty;

    [ObservableProperty]
    private string tags = string.Empty;

    [ObservableProperty]
    private string status = "Ready";

    [ObservableProperty]
    private string error = string.Empty;

    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(StatusColor));

    public string StatusColor => Status switch
    {
        "Generated" => "#4ADE80",
        "Waiting"   => "#FCD34D",
        "Pending"   => "#94A3B8",
        _           => "#F87171"
    };
}
