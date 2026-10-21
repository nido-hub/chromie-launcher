using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

namespace ChromieLauncher.Core;

public sealed class Storage
{
    public string Root { get; }
    public Storage(string? root = null)
    {
        Root = root ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ChromieLauncher");
        Directory.CreateDirectory(Root);
    }

    public Settings LoadSettings() => Load("settings.json", AppJsonContext.Default.Settings) ?? new();
    public void SaveSettings(Settings settings) => Save("settings.json", settings, AppJsonContext.Default.Settings);
    public T? Load<T>(string name, JsonTypeInfo<T> type) => File.Exists(Path.Combine(Root, name))
        ? JsonSerializer.Deserialize(File.ReadAllText(Path.Combine(Root, name)), type) : default;
    public void Save<T>(string name, T value, JsonTypeInfo<T> type) => AtomicWrite(Path.Combine(Root, name), JsonSerializer.Serialize(value, type));

    public string Profile(string gamePath)
    {
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(Path.GetFullPath(gamePath).TrimEnd('\\', '/').ToUpperInvariant())))[..20];
        var directory = Path.Combine(Root, "profiles", key);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static void AtomicWrite(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, content);
            File.Move(temporary, path, true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}

public static class SafePath
{
    public static string Child(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':') ||
            relative.Split('/', '\\').Any(p => p is ".." or "." or "" || IsDeviceName(p) || p.EndsWith('.') || p.EndsWith(' ') ||
                p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0))
            throw new InvalidDataException("Path is not allowed: " + relative);
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var result = Path.GetFullPath(Path.Combine(fullRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!result.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Path is outside the allowed folder.");
        RejectLinks(result);
        return result;
    }

    public static void RejectLinks(string path)
    {
        for (var current = Path.GetFullPath(path); !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) &&
                (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Symbolic links and junctions are not allowed: " + current);
    }

    public static void FolderName(string value)
    {
        if (value.Contains('/') || value.Contains('\\')) throw new InvalidDataException("Invalid folder name.");
        _ = Child(Path.GetTempPath(), value);
        if (value.StartsWith("Blizzard_", StringComparison.OrdinalIgnoreCase) || value.Equals("APIDocumentation", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Game client folders cannot be managed.");
    }

    private static bool IsDeviceName(string value)
    {
        var stem = value.Split('.')[0].ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" ||
            (stem.Length == 4 && (stem.StartsWith("COM") || stem.StartsWith("LPT")) && stem[3] is >= '1' and <= '9');
    }
}
