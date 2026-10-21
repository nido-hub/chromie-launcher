using System.Reflection;
using System.Text.Json;

namespace ChromieLauncher.Core;

public static class CatalogService
{
    public static Catalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChromieLauncher.Core.Data.catalog.json")!;
        return JsonSerializer.Deserialize(stream, AppJsonContext.Default.Catalog) ?? throw new InvalidDataException("Catalog is missing.");
    }

    public static string Revision(Catalog catalog, AddonPackage package) => string.IsNullOrEmpty(package.Commit) ? catalog.Commit : package.Commit;

    public static void ValidateRepository(string repository)
    {
        var parts = repository.Split('/');
        if (parts.Length != 2 || parts.Any(part => part.Length == 0 || part is "." or ".." ||
            !part.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.')))
            throw new InvalidDataException("Invalid GitHub repository.");
    }

    public static List<AddonPackage> Resolve(Catalog catalog, string id)
    {
        var resolved = new List<AddonPackage>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string current)
        {
            if (!visited.Add(current)) return;
            var package = catalog.Packages.SingleOrDefault(p => p.Id.Equals(current, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Dependency is missing from the catalog: " + current);
            foreach (var dependency in package.Dependencies) Visit(dependency);
            resolved.Add(package);
        }
        Visit(id);
        return resolved;
    }
}
