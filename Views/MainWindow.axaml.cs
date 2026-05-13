using System.Collections.Specialized;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using AnkiEnglishCardsBuilder.ViewModels;

namespace AnkiEnglishCardsBuilder.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
        Opened += async (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                await viewModel.InitializeAsync();
                viewModel.Logs.CollectionChanged += OnLogsChanged;
                viewModel.PropertyChanged += OnViewModelPropertyChanged;
            }
        };
        Closed += (_, _) =>
        {
            if (DataContext is MainWindowViewModel viewModel)
            {
                viewModel.Logs.CollectionChanged -= OnLogsChanged;
                viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            }
        };
    }

    private void OnLogsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        LogScrollViewer.ScrollToEnd();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.SelectedCard))
        {
            ScrollSelectedCardIntoView();
        }
    }

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not MainWindowViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.F && e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            viewModel.OpenSearch();
            FocusSearchBox();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && viewModel.IsSearchVisible)
        {
            viewModel.CloseSearch();
            e.Handled = true;
        }
    }

    private void FocusSearchBox()
    {
        Dispatcher.UIThread.Post(() =>
        {
            SearchTextBox.Focus();
            SearchTextBox.SelectAll();
        });
    }

    private void ScrollSelectedCardIntoView()
    {
        if (DataContext is not MainWindowViewModel { SelectedCard: not null } viewModel)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
            CardsDataGrid.ScrollIntoView(viewModel.SelectedCard, CardsDataGrid.Columns.FirstOrDefault()));
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
