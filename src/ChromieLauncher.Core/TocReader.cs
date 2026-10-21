using System.Text.RegularExpressions;

namespace ChromieLauncher.Core;

public static partial class TocReader
{
    public static Dictionary<string, string> Parse(string text)
    {
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in text.Split('\n'))
        {
            var match = Header().Match(line.TrimStart('\uFEFF'));
            if (match.Success) metadata[match.Groups[1].Value.Trim()] = match.Groups[2].Value.Trim();
        }
        return metadata;
    }

    public static string Clean(string value) => Color().Replace(value, "");

    public static void ValidateCompatibility(string text)
    {
        var metadata = Parse(text);
        if (!metadata.TryGetValue("Interface", out var version) ||
            !version.Split(',', ' ').Any(v => int.TryParse(v, out var n) && n >= 30000 && n <= 30300))
            throw new InvalidDataException("This addon does not declare a WoW 3.x interface compatible with 3.3.5a.");
    }

    [GeneratedRegex(@"^\s*##\s*([^:]+):\s*(.*)$")]
    private static partial Regex Header();
    [GeneratedRegex(@"\|c[0-9a-fA-F]{8}|\|r")]
    private static partial Regex Color();
}
