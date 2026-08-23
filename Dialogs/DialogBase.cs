using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace NightOwls.Dialogs;

public class DialogBase : Window
{
    protected static readonly Brush BgCard = new SolidColorBrush(Color.FromRgb(0x17, 0x17, 0x1E));
    protected static readonly Brush BrushBorder = new SolidColorBrush(Color.FromRgb(0x26, 0x26, 0x2F));
    protected static readonly Brush TextPrimary = new SolidColorBrush(Color.FromRgb(0xF2, 0xEF, 0xE9));
    protected static readonly Brush TextSecondary = new SolidColorBrush(Color.FromRgb(0x9B, 0x98, 0xA3));
    protected static readonly Brush Gold = new SolidColorBrush(Color.FromRgb(0xD9, 0xA4, 0x41));
    protected static readonly Brush GoldText = new SolidColorBrush(Color.FromRgb(0x14, 0x10, 0x07));

    protected DialogBase(string title)
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        SizeToContent = SizeToContent.WidthAndHeight;
        Background = Brushes.Transparent;
        AllowsTransparency = true;
        FontFamily = new FontFamily("Segoe UI");
        Title = title;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;
        MinWidth = 440;
        MaxWidth = 580;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape) Close();
        };
    }

    protected void SetContent(FrameworkElement body)
    {
        Content = new Border
        {
            Background = BgCard,
            BorderBrush = BrushBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(12),
            Padding = new Thickness(28, 22, 28, 24),
            Margin = new Thickness(18),
            Effect = new DropShadowEffect
            {
                BlurRadius = 32,
                ShadowDepth = 8,
                Opacity = 0.7,
                Color = Colors.Black
            },
            Child = body
        };
    }

    protected static TextBlock MakeTitle(string text) => new()
    {
        Text = text,
        FontSize = 16.5,
        FontWeight = FontWeights.SemiBold,
        Foreground = TextPrimary,
        Margin = new Thickness(0, 0, 0, 10),
        TextWrapping = TextWrapping.Wrap
    };

    protected static TextBlock MakeMessage(string text) => new()
    {
        Text = text,
        FontSize = 13,
        Foreground = TextSecondary,
        LineHeight = 20,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 20)
    };

    protected static Button MakeGoldButton(string text)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(16, 8, 16, 8),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = GoldText,
            Background = Gold,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand,
            IsDefault = true
        };
        b.Template = MakeButtonTemplate(filled: true);
        return b;
    }

    protected static Button MakeOutlineButton(string text, bool isCancel = true)
    {
        var b = new Button
        {
            Content = text,
            Padding = new Thickness(16, 8, 16, 8),
            FontSize = 12.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = TextPrimary,
            Background = Brushes.Transparent,
            BorderBrush = BrushBorder,
            BorderThickness = new Thickness(1),
            Cursor = System.Windows.Input.Cursors.Hand,
            IsCancel = isCancel
        };
        b.Template = MakeButtonTemplate(filled: false);
        return b;
    }

    private static ControlTemplate MakeButtonTemplate(bool filled)
    {
        var template = new ControlTemplate(typeof(Button));
        var factory = new FrameworkElementFactory(typeof(Border), "bg");
        factory.SetValue(Border.CornerRadiusProperty, new CornerRadius(7));
        factory.SetBinding(Border.BackgroundProperty,
            new System.Windows.Data.Binding("Background") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        if (!filled)
        {
            factory.SetValue(Border.BorderBrushProperty, BrushBorder);
            factory.SetValue(Border.BorderThicknessProperty, new Thickness(1));
        }
        var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
        presenter.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        presenter.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
        presenter.SetBinding(ContentPresenter.MarginProperty,
            new System.Windows.Data.Binding("Padding") { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        factory.AppendChild(presenter);
        template.VisualTree = factory;

        var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
        hover.Setters.Add(new Setter(Border.BackgroundProperty,
            filled ? new SolidColorBrush(Color.FromRgb(0xF0, 0xC6, 0x74)) : new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)), "bg"));
        template.Triggers.Add(hover);

        return template;
    }
}
