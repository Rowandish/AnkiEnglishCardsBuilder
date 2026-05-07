using System.Collections.Specialized;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using AnkiEnglishCardsBuilder.ViewModels;

namespace AnkiEnglishCardsBuilder.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.InitializeAsync();
                viewModel.Logs.CollectionChanged += OnLogsChanged;
            }
        };
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        LogScrollViewer.ScrollToEnd();
    }

    private async void OpenSettings_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var settingsViewModel = SettingsWindowViewModel.FromSettings(viewModel.Settings);
        var window = new SettingsWindow
        {
            DataContext = settingsViewModel
        };

        var result = await window.ShowDialog<SettingsWindowViewModel?>(this);
        if (result is not null)
        {
            await viewModel.SaveSettingsAsync(result.ToSettings());
        }
    }

    private async void Export_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        var storageProvider = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storageProvider is null)
        {
            return;
        }

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export Anki TSV",
            SuggestedFileName = "anki-english-cards.tsv",
            DefaultExtension = "tsv",
            FileTypeChoices =
            [
                new FilePickerFileType("TSV for Anki")
                {
                    Patterns = ["*.tsv", "*.txt"]
                }
            ]
        });

        if (file is not null)
        {
            await viewModel.ExportAsync(file.Path.LocalPath);
        }
    }
}
