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

    [ObservableProperty]
    private string inputText = "abroad, reliable, effort\nborrow, improve, thoughtful";

    [ObservableProperty]
    private string statusMessage = "Incolla le parole, poi genera le card.";

    [ObservableProperty]
    private string detailMessage = "Le parole possono essere separate da virgole, punto e virgola o nuove righe.";

    [ObservableProperty]
    private bool isBusy;

    [ObservableProperty]
    private AppSettings settings = new();

    public ObservableCollection<AnkiCard> Cards { get; } = [];

    public bool HasCards => Cards.Count > 0;

    public string ProviderSummary => $"{Settings.Provider} - {Settings.OpenAI.Model}";

    public async Task InitializeAsync()
    {
        Settings = await settingsStorage.LoadAsync();
        OnPropertyChanged(nameof(ProviderSummary));
    }

    public async Task SaveSettingsAsync(AppSettings updatedSettings)
    {
        Settings = updatedSettings;
        await settingsStorage.SaveAsync(Settings);
        OnPropertyChanged(nameof(ProviderSummary));
        StatusMessage = "Settings salvati.";
        DetailMessage = "Le prossime generazioni useranno provider e modello selezionati.";
    }

    [RelayCommand]
    private void ParseOnly()
    {
        var words = wordParser.Parse(InputText);
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
            ? "Nessuna parola trovata."
            : $"Trovate {words.Count} parole uniche.";
        DetailMessage = words.Count == 0
            ? "Aggiungi parole nella textbox prima di procedere."
            : "Puoi esportarle vuote o generare significati e frasi con il provider configurato.";
        OnPropertyChanged(nameof(HasCards));
    }

    [RelayCommand]
    private async Task GenerateAsync()
    {
        var words = wordParser.Parse(InputText);
        if (words.Count == 0)
        {
            StatusMessage = "Nessuna parola da generare.";
            DetailMessage = "Inserisci almeno una parola inglese.";
            return;
        }

        IsBusy = true;
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

            OnPropertyChanged(nameof(HasCards));
            var progress = new Progress<string>(message =>
            {
                StatusMessage = message;
                DetailMessage = "Se la rete e' lenta, l'app resta reattiva. Puoi annullare la generazione.";
            });

            var provider = providerFactory.Create(Settings);
            var result = await provider.EnrichAsync(words, progress, generationCts.Token);

            Cards.Clear();
            foreach (var card in result.Cards)
            {
                Cards.Add(card);
            }

            StatusMessage = $"Generate {Cards.Count} card.";
            DetailMessage = result.Warnings.Count == 0
                ? "Controlla rapidamente la preview, poi esporta il TSV per Anki."
                : string.Join(Environment.NewLine, result.Warnings);
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Generazione annullata.";
            DetailMessage = "Le card parziali restano modificabili.";
        }
        catch (Exception ex)
        {
            StatusMessage = "Generazione non completata.";
            DetailMessage = ExplainException(ex);

            foreach (var card in Cards)
            {
                if (card.Status is "Waiting" or "Pending")
                {
                    card.Status = "Needs review";
                    card.Error = "Generazione interrotta: " + ex.Message;
                }
            }
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasCards));
        }
    }

    [RelayCommand]
    private void CancelGeneration()
    {
        generationCts?.Cancel();
        StatusMessage = "Annullamento in corso...";
        DetailMessage = "Attendo che l'operazione corrente rilasci la connessione.";
    }

    public async Task ExportAsync(string path)
    {
        if (Cards.Count == 0)
        {
            StatusMessage = "Nessuna card da esportare.";
            DetailMessage = "Genera o prepara almeno una riga prima dell'export.";
            return;
        }

        try
        {
            await exporter.ExportAsync(Cards, path, CancellationToken.None);
            StatusMessage = "Export completato.";
            DetailMessage = $"File pronto per Anki: {path}";
        }
        catch (Exception ex)
        {
            StatusMessage = "Export non riuscito.";
            DetailMessage = ex.Message;
        }
    }

    private static string ExplainException(Exception ex)
    {
        return ex switch
        {
            InvalidOperationException => ex.Message,
            HttpRequestException => "Non riesco a raggiungere il provider. Controlla la connessione internet, firewall/proxy e riprova.",
            TaskCanceledException => "La richiesta e' scaduta. Aumenta il timeout nei Settings o riprova con meno parole.",
            _ => ex.Message
        };
    }
}
