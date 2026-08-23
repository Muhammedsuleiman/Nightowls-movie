using System.Windows;
using NightOwls.ViewModels;

namespace NightOwls.Helpers;

public class DialogService
{
    public static bool ResumeRequested { get; set; }

    public long? AskResume(MovieCardViewModel card)
    {
        var dialog = new Dialogs.ResumeDialog(
            title: card.TitleDisplay,
            message: $"You watched this until {FormatClock(card.ResumePositionMs)}. Continue from there?",
            continueText: "Continue Watching",
            restartText: "Start Over");
        dialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current.MainWindow;
        var result = dialog.ShowDialog();
        if (result == true) return card.ResumePositionMs;
        if (result == false) return 0;
        return null;
    }

    public bool Confirm(string title, string message, string confirmText = "Confirm", bool destructive = false)
    {
        var dialog = new Dialogs.ConfirmDialog(title, message, confirmText, destructive);
        dialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current.MainWindow;
        return dialog.ShowDialog() == true;
    }

    public void Info(string title, string message)
    {
        var dialog = new Dialogs.MessageDialog(title, message);
        dialog.Owner = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive)
                       ?? Application.Current.MainWindow;
        dialog.ShowDialog();
    }

    private static string FormatClock(long ms)
    {
        var t = TimeSpan.FromMilliseconds(ms);
        return t.TotalHours >= 1 ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }
}
