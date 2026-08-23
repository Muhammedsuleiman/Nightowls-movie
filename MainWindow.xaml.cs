using System.Windows;
using System.Windows.Input;
using NightOwls.Services;
using NightOwls.ViewModels;

namespace NightOwls;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        StateChanged += (_, _) =>
        {
            if (maxGlyph is not null)
                maxGlyph.Text = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            Maximize_Click(sender, e);
            return;
        }
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            try { DragMove(); } catch { }
        }
    }

    private void Minimize_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void Maximize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        try { App.Playback.PauseAndPersist(); } catch { }
        try
        {
            App.SettingsService.RestoredWindowSize = new Size(ActualWidth, ActualHeight);
        }
        catch { }
        Close();
    }
}
