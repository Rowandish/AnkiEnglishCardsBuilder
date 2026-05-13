using System.Collections.ObjectModel;
using System.Net.Http;
using AnkiEnglishCardsBuilder.Models;
using AnkiEnglishCardsBuilder.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AnkiEnglishCardsBuilder.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly WordParser wordParser = new();
    private readonly AnkiTsvExporter exporter = new();
    private readonly SettingsStorage settingsStorage = new();
    private readonly CardEnrichmentProviderFactory providerFactory = new();
    private CancellationTokenSource? generationCts;

    public MainWindowViewModel()
    {
        Cards.CollectionChanged += (_, _) => UpdateSearchResult();
    }

    [ObservableProperty]
    private string inputText = "abroad, reliable, effort\nborrow, improve, thoughtful";

    public int WordCount => wordParser.Parse(InputText).Count;

    partial void OnInputTextChanged(string value) => OnPropertyChanged(nameof(WordCount));

    [ObservableProperty]
    private string statusMessage = "Paste words, then generate cards.";

    [ObservableProperty]
    private string detailMessage = "Words can be separated by commas, semicolons, or new lines.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private int progressCurrent;

    [ObservableProperty]
    private int progressTotal;

    public double ProgressPercent => ProgressTotal == 0 ? 0 : (double)ProgressCurrent / ProgressTotal * 100;

    [ObservableProperty]
    private bool isLogPanelVisible;

    [ObservableProperty]
    private AppSettings settings = new();

    [ObservableProperty]
    private bool isSearchVisible;

    [ObservableProperty]
    private string searchText = string.Empty;

    [ObservableProperty]
    private string searchStatusMessage = string.Empty;

    [ObservableProperty]
    private string searchStatusColor = "#7EA6D9";

    [ObservableProperty]
    private AnkiCard? selectedCard;

    public ObservableCollection<AnkiCard> Cards { get; } = [];

    public ObservableCollection<string> Logs { get; } = [];

    public bool HasCards => Cards.Count > 0;

    public int StatsTotal => Cards.Count;
    public int StatsGenerated => Cards.Count(c => c.Status == "Generated");
    public int StatsErrors => Cards.Count(c => c.Status == "Needs review");

    private void RefreshCardStats()
    {
        OnPropertyChanged(nameof(StatsTotal));
        OnPropertyChanged(nameof(StatsGenerated));
        OnPropertyChanged(nameof(StatsErrors));
        OnPropertyChanged(nameof(HasCards));
    }

    public string ProviderSummary => $"{Settings.Provider} - {Settings.OpenAI.Model}";

    partial void OnSearchTextChanged(string value) => UpdateSearchResult();

    public async Task InitializeAsync()
    {
        AddLog("Starting application and loading settings.");
        Settings = await settingsStorage.LoadAsync();
        OnPropertyChanged(nameof(ProviderSummary));
        AddLog($"Settings loaded: provider {Settings.Provider}, model {Settings.OpenAI.Model}, timeout {Settings.OpenAI.TimeoutSeconds}s.");
    }

    public async Task SaveSettingsAsync(AppSettings updatedSettings)
    {
        Settings = updatedSettings;
        await settingsStorage.SaveAsync(Settings);
        OnPropertyChanged(nameof(ProviderSummary));
        StatusMessage = "Settings saved.";
        DetailMessage = "Future generations will use the selected provider and model.";
        AddLog($"Settings saved: provider {Settings.Provider}, model {Settings.OpenAI.Model}, timeout {Settings.OpenAI.TimeoutSeconds}s, batch {Settings.OpenAI.BatchSize}.");
    }

    [RelayCommand]
    private void ParseOnly()
    {
        var parseResult = ParseAndNormalizeInput();
        var words = parseResult.Words;
        AddLog($"Parsed input: found {words.Count} unique words.");
        AddDuplicateLog(parseResult);
        Cards.Clear();

        foreach (var word in words)
        {
            Cards.Add(new AnkiCard
            {
                Word = word,
                Status = "Pending"
            });
        }

        StatusMessage = words.Count == 0
            ? "No words found."
            : $"Found {words.Count} unique words.";
        DetailMessage = words.Count == 0
            ? "Add words to the text box before continuing."
            : "You can export empty cards or generate meanings and examples with the configured provider.";
        RefreshCardStats();
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        var parseResult = ParseAndNormalizeInput();
        var words = parseResult.Words;
        AddLog($"Generation requested: {words.Count} unique candidate words.");
        AddDuplicateLog(parseResult);
        if (words.Count == 0)
        {
            StatusMessage = "No words to generate.";
            DetailMessage = "Enter at least one English word.";
            return;
        }

        IsBusy = true;
        ProgressCurrent = 0;
        ProgressTotal = words.Count;
        OnPropertyChanged(nameof(ProgressPercent));
        Cards.Clear();
        generationCts?.Dispose();
        generationCts = new CancellationTokenSource();

        try
        {
            foreach (var word in words)
            {
                Cards.Add(new AnkiCard
                {
                    Word = word,
                    Status = "Waiting"
                });
            }

            RefreshCardStats();
            var progress = new Progress<ProgressReport>(report =>
            {
                StatusMessage = report.Message;
                DetailMessage = "If the network is slow, the app stays responsive. You can cancel generation.";
                ProgressCurrent = report.Completed;
                OnPropertyChanged(nameof(ProgressPercent));
                AddLog(report.Message);
            });

            var provider = providerFactory.Create(Settings);
            AddLog($"Using provider {provider.Name} with model {Settings.OpenAI.Model}.");
            var result = await provider.EnrichAsync(words, progress, generationCts.Token);

            Cards.Clear();
            foreach (var card in result.Cards)
            {
                Cards.Add(card);
            }

            ProgressCurrent = ProgressTotal;
            OnPropertyChanged(nameof(ProgressPercent));
            StatusMessage = $"Generated {Cards.Count} cards.";
            DetailMessage = result.Warnings.Count == 0
                ? "Review the preview, then export the TSV for Anki."
                : string.Join(Environment.NewLine, result.Warnings);
            AddLog($"Generation completed: {Cards.Count} cards, {result.Warnings.Count} warnings.");
            foreach (var warning in result.Warnings)
            {
                AddLog("Warning: " + warning);
            }
        }
        catch (TimeoutException ex)
        {
            StatusMessage = "OpenAI request timed out.";
            DetailMessage = ExplainException(ex);
            AddLog("OpenAI request timeout: " + ExplainException(ex));

            MarkPendingCardsAsNeedsReview("OpenAI request timeout: " + ex.Message);
        }
        catch (OperationCanceledException ex)
        {
            if (generationCts?.IsCancellationRequested == true)
            {
                StatusMessage = "Generation canceled.";
                DetailMessage = "Partial cards remain editable.";
                AddLog("Generation canceled by the user.");
            }
            else
            {
                StatusMessage = "Generation interrupted.";
                DetailMessage = "The OpenAI request was interrupted before a response arrived. Try again or increase the timeout in Settings.";
                AddLog("OpenAI request interrupted without manual cancellation: " + ex.Message);
                MarkPendingCardsAsNeedsReview("OpenAI request interrupted: " + ex.Message);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = "Generation did not complete.";
            DetailMessage = ExplainException(ex);
            AddLog("Generation error: " + ExplainException(ex));

            MarkPendingCardsAsNeedsReview("Generation interrupted: " + ex.Message);
        }
        finally
        {
            IsBusy = false;
            RefreshCardStats();
        }
    }

    [RelayCommand]
    private void CancelGeneration()
    {
        generationCts?.Cancel();
        StatusMessage = "Canceling...";
        DetailMessage = "Waiting for the current operation to release the connection.";
        AddLog("Generation cancellation requested.");
    }

    [RelayCommand]
    private void ToggleLogPanel()
    {
        IsLogPanelVisible = !IsLogPanelVisible;
        AddLog(IsLogPanelVisible ? "Log panel opened." : "Log panel closed.");
    }

    public void OpenSearch()
    {
        IsSearchVisible = true;
        UpdateSearchResult();
    }

    public void CloseSearch()
    {
        IsSearchVisible = false;
    }

    public async Task ExportAsync(string path)
    {
        if (Cards.Count == 0)
        {
            StatusMessage = "No cards to export.";
            DetailMessage = "Generate or prepare at least one row before exporting.";
            return;
        }

        try
        {
            await exporter.ExportAsync(Cards, path, CancellationToken.None);
            StatusMessage = "Export completed.";
            DetailMessage = $"File ready for Anki: {path}";
            AddLog($"TSV export completed: {path}");
        }
        catch (Exception ex)
        {
            StatusMessage = "Export failed.";
            DetailMessage = ex.Message;
            AddLog("Export error: " + ex.Message);
        }
    }

    private void AddLog(string message)
    {
        Logs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
    }

    private WordParseResult ParseAndNormalizeInput()
    {
        var result = wordParser.ParseWithStats(InputText);
        var normalizedInput = string.Join(Environment.NewLine, result.Words);

        if (!string.Equals(InputText.Trim(), normalizedInput, StringComparison.Ordinal))
        {
            InputText = normalizedInput;
        }

        return result;
    }

    private void AddDuplicateLog(WordParseResult result)
    {
        if (result.DuplicatesRemoved > 0)
        {
            AddLog($"Duplicates removed before LLM request: {result.DuplicatesRemoved}.");
        }
    }

    private void MarkPendingCardsAsNeedsReview(string error)
    {
        foreach (var card in Cards)
        {
            if (card.Status is "Waiting" or "Pending")
            {
                card.Status = "Needs review";
                card.Error = error;
            }
        }
    }

    private void UpdateSearchResult()
    {
        var query = SearchText.Trim();

        if (Cards.Count == 0)
        {
            SelectedCard = null;
            SearchStatusMessage = "No cards in the list.";
            SearchStatusColor = "#7EA6D9";
            return;
        }

        if (string.IsNullOrWhiteSpace(query))
        {
            SelectedCard = null;
            SearchStatusMessage = "Type a word to search.";
            SearchStatusColor = "#7EA6D9";
            return;
        }

        var exactMatch = Cards.FirstOrDefault(card =>
            string.Equals(card.Word.Trim(), query, StringComparison.OrdinalIgnoreCase));
        var partialMatch = exactMatch ?? Cards.FirstOrDefault(card =>
            card.Word.Contains(query, StringComparison.OrdinalIgnoreCase));

        SelectedCard = partialMatch;
        SearchStatusMessage = partialMatch is null
            ? "Not present in the list."
            : exactMatch is null
                ? $"Match: {partialMatch.Word}"
                : $"Found: {partialMatch.Word}";
        SearchStatusColor = partialMatch is null ? "#F87171" : "#4ADE80";
    }

    private static string ExplainException(Exception ex)
    {
        return ex switch
        {
            InvalidOperationException => ex.Message,
            HttpRequestException => "The provider could not be reached. Check your internet connection, firewall/proxy, and try again.",
            TimeoutException => ex.Message + " Increase the timeout in Settings or try smaller batches.",
            TaskCanceledException => "The request timed out. Increase the timeout in Settings or try fewer words.",
            _ => ex.Message
        };
    }
}
