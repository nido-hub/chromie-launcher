namespace ChromieLauncher.Core;

public sealed record ClientPatch(string Id, string Name, bool Hd, string ShareUrl, string SourceName, long Size, string Sha256);
public sealed record PatchStatus(bool Exists, string? VariantId);
public sealed record TransferProgress(long Completed, long Total);
public sealed record MegaDownload(string Name, long Size, string Sha256);

public static class ClientPatchCatalog
{
    public const string FileName = "patch-y.mpq";
    public static IReadOnlyList<ClientPatch> All { get; } = Array.AsReadOnly(new[]
    {
        new ClientPatch("patch-y-standard", "Patch Y — non-HD client", false,
            "https://mega.nz/file/nmBkjBzZ#dN1_3LnBzpF1cSfIqtMTB4im--VmG6ocRBxYFgpVg24",
            "patch-y.mpq", 27780537, "d42f902107669f76b04b40e6c92d2869f18d52df00de6f7cf064cd749fe4cc17"),
        new ClientPatch("patch-y-hd", "Patch Y — HD client", true,
            "https://mega.nz/file/GqQHVD6Z#8SgtiuJfIvyEkO2MbUPBK7e0tYWabJMyz1Jft6jyH_w",
            "patch-y - andre hd - new spells.mpq", 28645259, "7535d02ee3bf6d3722f3bd970fc4b24fce41c3018ddca1f2732cb7563202a39d")
    });
}
