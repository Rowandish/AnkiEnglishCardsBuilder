using Avalonia.Controls;
using AnkiEnglishCardsBuilder.ViewModels;

namespace AnkiEnglishCardsBuilder.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent();
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(DataContext as SettingsWindowViewModel);
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }
}
