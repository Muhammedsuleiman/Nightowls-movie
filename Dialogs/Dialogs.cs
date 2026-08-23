using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace NightOwls.Dialogs;

public sealed class ResumeDialog : DialogBase
{
    public ResumeDialog(string title, string message, string continueText, string restartText)
        : base(title)
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeTitle("Continue watching?"));
        panel.Children.Add(MakeMessage($"{title}\n\n{message}"));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cont = MakeGoldButton(continueText);
        cont.Click += (_, _) => DialogResult = true;
        var restart = MakeOutlineButton(restartText, isCancel: false);
        restart.Click += (_, _) => DialogResult = false;
        buttons.Children.Add(cont);
        buttons.Children.Add(restart);
        restart.Margin = new Thickness(10, 0, 0, 0);
        panel.Children.Add(buttons);

        SetContent(panel);
    }
}

public sealed class ConfirmDialog : DialogBase
{
    public ConfirmDialog(string title, string message, string confirmText, bool destructive)
        : base(title)
    {
        var panel = new StackPanel();
        if (destructive)
            panel.Children.Add(new TextBlock
            {
                Text = "!",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x5C, 0x5C)),
                Margin = new Thickness(0, 0, 0, 8)
            });
        panel.Children.Add(MakeTitle(title));
        panel.Children.Add(MakeMessage(message));

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        var cancel = MakeOutlineButton("Cancel");
        cancel.Click += (_, _) => DialogResult = false;
        var ok = destructive ? ConfirmDangerButton(confirmText) : MakeGoldButton(confirmText);
        ok.Click += (_, _) => DialogResult = true;
        buttons.Children.Add(cancel);
        buttons.Children.Add(ok);
        ok.Margin = new Thickness(10, 0, 0, 0);
        panel.Children.Add(buttons);

        SetContent(panel);
    }

    private static Button ConfirmDangerButton(string text)
    {
        var b = MakeOutlineButton(text, isCancel: false);
        b.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0x5C, 0x5C));
        return b;
    }
}

public sealed class MessageDialog : DialogBase
{
    public MessageDialog(string title, string message) : base(title)
    {
        var panel = new StackPanel();
        panel.Children.Add(MakeTitle(title));
        panel.Children.Add(MakeMessage(message));
        var ok = MakeGoldButton("OK");
        ok.HorizontalAlignment = HorizontalAlignment.Right;
        ok.Click += (_, _) => Close();
        panel.Children.Add(ok);
        SetContent(panel);
    }
}
