using System.Reflection;
using System.Text.Json;

namespace ChromieLauncher.Core;

public static class CatalogService
{
    public static Catalog Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ChromieLauncher.Core.Data.catalog.json")!;
        return JsonSerializer.Deserialize(stream, AppJsonContext.Default.Catalog) ?? throw new InvalidDataException("Catalogue absent.");
    }

    public static List<AddonPackage> Resolve(Catalog catalog, string id)
    {
        var resolved = new List<AddonPackage>();
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Visit(string current)
        {
            if (!visited.Add(current)) return;
            var package = catalog.Packages.SingleOrDefault(p => p.Id.Equals(current, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidDataException("Dépendance absente du catalogue : " + current);
            foreach (var dependency in package.Dependencies) Visit(dependency);
            resolved.Add(package);
        }
        Visit(id);
        return resolved;
    }
}
