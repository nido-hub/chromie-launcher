using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace ChromieLauncher.Desktop;

/// <summary>Local, offline visual identity inspired by the ChromieCraft website.</summary>
internal static class LauncherTheme
{
    public static readonly FontFamily BodyFont = new("avares://ChromieLauncher/Assets/Fonts#Roboto Condensed");
    public static readonly FontFamily HeadingFont = new("avares://ChromieLauncher/Assets/Fonts#Marcellus SC");
    public static readonly IBrush Background = Brush.Parse("#0E0E0E");
    public static readonly IBrush Surface = Brush.Parse("#E80E1012");
    public static readonly IBrush Border = Brush.Parse("#55504A");
    public static readonly IBrush Text = Brush.Parse("#F0EEEA");
    public static readonly IBrush Muted = Brush.Parse("#B7B6B1");
    public static readonly IBrush Gold = Brush.Parse("#D8BD88");
    public static readonly IBrush Ice = Brush.Parse("#A0C9DA");
    public static readonly IBrush Success = Brush.Parse("#A2C7AE");
    private static readonly Dictionary<string, Bitmap> Bitmaps = [];

    public static Bitmap Bitmap(string name)
    {
        if (!Bitmaps.TryGetValue(name, out var bitmap))
        {
            using var source = AssetLoader.Open(new Uri("avares://ChromieLauncher/Assets/" + name));
            Bitmaps.Add(name, bitmap = new Bitmap(source));
        }
        return bitmap;
    }

    public static Image Image(string name, double width, double height) => new()
    {
        Source = Bitmap(name), Width = width, Height = height,
        Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center
    };

    public static LinearGradientBrush Shade(params (double Offset, string Color)[] stops)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
            EndPoint = new RelativePoint(0, 1, RelativeUnit.Relative)
        };
        foreach (var stop in stops) brush.GradientStops.Add(new GradientStop(Color.Parse(stop.Color), stop.Offset));
        return brush;
    }
}
