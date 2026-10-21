using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Themes.Fluent;
using Avalonia.Styling;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Controls.Presenters;

namespace ChromieLauncher.Desktop;

public sealed class App : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
        foreach (var key in new[] { "SystemAccentColor", "SystemAccentColorLight1", "SystemAccentColorLight2", "SystemAccentColorLight3", "SystemAccentColorDark1", "SystemAccentColorDark2", "SystemAccentColorDark3" })
            Resources[key] = Color.Parse("#D8BD88");
        Styles.Add(new Style(s => s.OfType<Button>())
        {
            Setters =
            {
                new Setter(Button.CornerRadiusProperty, new CornerRadius(2)),
                new Setter(Button.PaddingProperty, new Thickness(16, 11)),
                new Setter(Button.FontFamilyProperty, LauncherTheme.HeadingFont),
                new Setter(Button.FontSizeProperty, 13d),
                new Setter(Button.BackgroundProperty, Brush.Parse("#E60E0E0E")),
                new Setter(Button.ForegroundProperty, LauncherTheme.Text),
                new Setter(Button.BorderBrushProperty, Brush.Parse("#68645C")),
                new Setter(Button.BorderThicknessProperty, new Thickness(1)),
                new Setter(Button.CursorProperty, new Avalonia.Input.Cursor(Avalonia.Input.StandardCursorType.Hand))
            }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("primary"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, Brush.Parse("#ED111110")),
                new Setter(Button.ForegroundProperty, Brushes.White),
                new Setter(Button.FontSizeProperty, 14d),
                new Setter(Button.BorderBrushProperty, Brush.Parse("#E3D9C4")),
                new Setter(Button.PaddingProperty, new Thickness(24, 13))
            }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("nav"))
        {
            Setters =
            {
                new Setter(Button.HorizontalContentAlignmentProperty, Avalonia.Layout.HorizontalAlignment.Center),
                new Setter(Button.BackgroundProperty, Brushes.Transparent),
                new Setter(Button.ForegroundProperty, LauncherTheme.Muted),
                new Setter(Button.BorderBrushProperty, Brushes.Transparent),
                new Setter(Button.BorderThicknessProperty, new Thickness(0, 0, 0, 2)),
                new Setter(Button.CornerRadiusProperty, new CornerRadius(0)),
                new Setter(Button.PaddingProperty, new Thickness(15, 17)),
                new Setter(Button.FontSizeProperty, 14d)
            }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("selected"))
        {
            Setters =
            {
                new Setter(Button.BackgroundProperty, Brush.Parse("#332E271C")),
                new Setter(Button.ForegroundProperty, LauncherTheme.Gold),
                new Setter(Button.BorderBrushProperty, LauncherTheme.Gold)
            }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class(":pointerover").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brush.Parse("#DE2B2924")), new Setter(ContentPresenter.BorderBrushProperty, LauncherTheme.Gold) }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class("primary").Class(":pointerover"))
        {
            Setters = { new Setter(Button.ForegroundProperty, LauncherTheme.Gold) }
        });
        Styles.Add(new Style(s => s.OfType<Button>().Class(":pressed").Template().OfType<ContentPresenter>().Name("PART_ContentPresenter"))
        {
            Setters = { new Setter(ContentPresenter.BackgroundProperty, Brush.Parse("#FF423B2E")), new Setter(ContentPresenter.BorderBrushProperty, Brushes.White) }
        });
        Styles.Add(new Style(s => s.OfType<TextBox>())
        {
            Setters =
            {
                new Setter(TextBox.FontFamilyProperty, LauncherTheme.BodyFont),
                new Setter(TextBox.FontSizeProperty, 14d),
                new Setter(TextBox.BackgroundProperty, Brush.Parse("#EB0E1012")),
                new Setter(TextBox.ForegroundProperty, LauncherTheme.Text),
                new Setter(TextBox.BorderBrushProperty, Brush.Parse("#68645C")),
                new Setter(TextBox.CornerRadiusProperty, new CornerRadius(2)),
                new Setter(TextBox.PaddingProperty, new Thickness(13, 11))
            }
        });
        Styles.Add(new Style(s => s.OfType<ComboBox>())
        {
            Setters =
            {
                new Setter(ComboBox.FontFamilyProperty, LauncherTheme.BodyFont),
                new Setter(ComboBox.FontSizeProperty, 14d),
                new Setter(ComboBox.BackgroundProperty, Brush.Parse("#EB0E1012")),
                new Setter(ComboBox.ForegroundProperty, LauncherTheme.Text),
                new Setter(ComboBox.BorderBrushProperty, Brush.Parse("#68645C")),
                new Setter(ComboBox.CornerRadiusProperty, new CornerRadius(2))
            }
        });
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop) desktop.MainWindow = new MainWindow();
        base.OnFrameworkInitializationCompleted();
    }
}
