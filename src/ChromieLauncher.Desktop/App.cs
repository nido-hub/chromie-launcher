using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Media;

namespace ChromieLauncher.Desktop;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        Styles.Add(new Style(s => s.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.CornerRadiusProperty, new CornerRadius(8)),
                new Setter(Button.PaddingProperty, new Thickness(16, 11)),
                new Setter(Button.CursorProperty, new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand))
            }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("primary"))
        {
            Setters = { new Setter(Button.BackgroundProperty, Brush.Parse("#A78BFA")), new Setter(Button.ForegroundProperty, Brush.Parse("#151120")), new Setter(Button.FontWeightProperty, FontWeight.SemiBold) }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("nav"))
        {
            Setters = { new Setter(Button.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Left), new Setter(Button.BackgroundProperty, Brushes.Transparent), new Setter(Button.ForegroundProperty, Brush.Parse("#A1A4B5")) }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("selected"))
        {
            Setters = { new Setter(Button.BackgroundProperty, Brush.Parse("#2A233B")), new Setter(Button.ForegroundProperty, Brush.Parse("#C4B5FD")) }
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
