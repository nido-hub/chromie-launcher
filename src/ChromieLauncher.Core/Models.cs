using System.Text.Json.Serialization;

namespace ChromieLauncher.Core;

public sealed class Catalog
{
    public string Commit { get; set; } = "";
    public string Source { get; set; } = "https://wowgaming.github.io/addon-archive/";
    public List<AddonPackage> Packages { get; set; } = [];
    public List<ArchiveFile> Files { get; set; } = [];
}

public sealed class AddonPackage
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Category { get; set; } = "Interface";
    public string Description { get; set; } = "";
    public string Version { get; set; } = "Version non renseignée";
    public List<string> Folders { get; set; } = [];
    public List<string> Dependencies { get; set; } = [];
}

public sealed class ArchiveFile
{
    public string Path { get; set; } = "";
    public string Sha { get; set; } = "";
    public long Size { get; set; }
}

public sealed class InstalledAddon
{
    public string Folder { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "Inconnue";
    public string Interface { get; set; } = "";
    public string Dependencies { get; set; } = "";
    public bool Managed { get; set; }
    public string PackageId { get; set; } = "";
    public string Status => Managed ? "Géré par le launcher" : "Installation manuelle";
}

public sealed class Settings
{
    public string GamePath { get; set; } = "";
    public string UpdateFeedUrl { get; set; } = "";
    public bool CheckUpdatesAtStartup { get; set; } = true;
}

public sealed class ManagedState
{
    public List<ManagedPackage> Packages { get; set; } = [];
}

public sealed class ManagedPackage
{
    public string Id { get; set; } = "";
    public string Commit { get; set; } = "";
    public List<string> Folders { get; set; } = [];
}

public sealed class SignedUpdate
{
    public string Payload { get; set; } = "";
    public string Signature { get; set; } = "";
}

public sealed class UpdateRelease
{
    public string Version { get; set; } = "";
    public string Url { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class UpdateJob
{
    public string TargetDirectory { get; set; } = "";
    public int ProcessId { get; set; }
    public long ProcessStartTicks { get; set; }
}

public sealed class MutationJournal
{
    public string PreviousState { get; set; } = "";
    public List<MutationFolder> Folders { get; set; } = [];
}

public sealed class MutationFolder
{
    public string Name { get; set; } = "";
    public bool Existed { get; set; }
}

[JsonSourceGenerationOptions(WriteIndented = true, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(Catalog))]
[JsonSerializable(typeof(Settings))]
[JsonSerializable(typeof(ManagedState))]
[JsonSerializable(typeof(SignedUpdate))]
[JsonSerializable(typeof(UpdateRelease))]
[JsonSerializable(typeof(UpdateJob))]
[JsonSerializable(typeof(MutationJournal))]
public partial class AppJsonContext : JsonSerializerContext;
