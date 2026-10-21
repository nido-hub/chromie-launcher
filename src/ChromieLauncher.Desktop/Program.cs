using Avalonia;
using ChromieLauncher.Core;

namespace ChromieLauncher.Desktop;

internal static class Program
{
    public static string? SmokeTestOutput { get; private set; }
    public static bool SmokeTestFailed { get; set; }
    private static Mutex? _instance;

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 2 && args[0] == "--apply-update") return UpdateService.Apply(args[1]);
        if (args.Length == 2 && args[0] == "--smoke-test") SmokeTestOutput = args[1];
        var instanceName = SmokeTestOutput is null ? "Local\\ChromieLauncher.Desktop" : "Local\\ChromieLauncher.SmokeTest." + Guid.NewGuid().ToString("N");
        using var instance = new Mutex(true, instanceName, out var created);
        if (!created) return 0;
        _instance = instance;
        try
        {
            LauncherText.SetLanguage(new Storage().LoadSettings().Language);
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
            return SmokeTestFailed ? 1 : 0;
        }
        catch (Exception exception)
        {
            var storage = new Storage();
            File.WriteAllText(Path.Combine(storage.Root, "crash.log"), exception.ToString());
            return 1;
        }
        finally { GC.KeepAlive(_instance); }
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
