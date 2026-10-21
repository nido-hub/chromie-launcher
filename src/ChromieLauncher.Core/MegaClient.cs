using System.Buffers.Binary;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ChromieLauncher.Core;

public interface IMegaDownloader
{
    Task<MegaDownload> DownloadAsync(string shareUrl, long expectedSize, Stream destination,
        IProgress<TransferProgress>? progress = null, CancellationToken token = default);
}

public sealed class MegaRequest
{
    [JsonPropertyName("a")] public string Action { get; set; } = "g";
    [JsonPropertyName("g")] public int Download { get; set; } = 1;
    [JsonPropertyName("ssl")] public int Ssl { get; set; } = 2;
    [JsonPropertyName("p")] public string Handle { get; set; } = "";
}

public sealed record MegaReference(string Handle, byte[] Key)
{
    public static MegaReference Parse(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != "https" || uri.Host != "mega.nz" ||
            uri.UserInfo.Length != 0 || !uri.IsDefaultPort || uri.Query.Length != 0)
            throw new InvalidDataException("A public HTTPS mega.nz file link is required.");
        var parts = uri.AbsolutePath.Split('/');
        var keyText = uri.Fragment.TrimStart('#');
        if (parts.Length != 3 || parts[1] != "file" || parts[2].Length != 8 || !IsUrlBase64(parts[2]) ||
            keyText.Length != 43 || !IsUrlBase64(keyText))
            throw new InvalidDataException("Invalid MEGA file link or decryption key.");
        return new(parts[2], Decode(keyText));
    }

    internal static byte[] Decode(string value) => Convert.FromBase64String(value.Replace('-', '+').Replace('_', '/').PadRight((value.Length + 3) / 4 * 4, '='));
    private static bool IsUrlBase64(string value) => value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
}

/// <summary>Public-file downloads only. No account, external executable or MEGA SDK.
/// File keys stay local: AES-CTR plaintext is authenticated with MEGA's chunk MAC.</summary>
public sealed class MegaClient(HttpClient http) : IMegaDownloader
{
    public const int MaximumFileSize = 64 * 1024 * 1024;

    public async Task<MegaDownload> DownloadAsync(string shareUrl, long expectedSize, Stream destination,
        IProgress<TransferProgress>? progress = null, CancellationToken token = default)
    {
        var reference = MegaReference.Parse(shareUrl);
        if (expectedSize <= 0 || expectedSize > MaximumFileSize) throw new InvalidDataException("Invalid patch download size.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromMinutes(15));
        var ct = timeout.Token;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://g.api.mega.co.nz/cs?id=" + Random.Shared.Next(1, int.MaxValue));
            request.Content = new ByteArrayContent(JsonSerializer.SerializeToUtf8Bytes(new[] { new MegaRequest { Handle = reference.Handle } }, AppJsonContext.Default.MegaRequestArray));
            request.Content.Headers.ContentType = new("application/json");
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            using var body = JsonDocument.Parse(await ReadLimitedAsync(response.Content, 128 * 1024, ct));
            if (body.RootElement.ValueKind != JsonValueKind.Array || body.RootElement.GetArrayLength() != 1)
                throw new InvalidDataException("Invalid MEGA API response.");
            var file = body.RootElement[0];
            if (file.ValueKind == JsonValueKind.Number)
                throw new IOException("MEGA refused this download (error " + file.GetInt32() + "). The link may be unavailable or the transfer quota may be exceeded.");
            if (file.ValueKind != JsonValueKind.Object || !file.TryGetProperty("s", out var sizeNode) ||
                !sizeNode.TryGetInt64(out var size) || size != expectedSize)
                throw new InvalidDataException("The MEGA file size no longer matches this patch. Installation refused.");
            if (!file.TryGetProperty("g", out var urlNode) || urlNode.ValueKind != JsonValueKind.String ||
                !Uri.TryCreate(urlNode.GetString(), UriKind.Absolute, out var downloadUrl))
                throw new IOException("MEGA did not provide a download URL. Try again later.");
            ValidateDownloadUri(downloadUrl);
            var name = MegaCrypto.AttributeName(file.GetProperty("at").GetString()!, reference.Key);
            using var download = await http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)download.StatusCode == 509 || download.StatusCode == HttpStatusCode.TooManyRequests)
                throw new IOException("MEGA's transfer limit has been reached. Please try again later.");
            download.EnsureSuccessStatusCode();
            if (download.Content.Headers.ContentLength is { } length && length != expectedSize)
                throw new InvalidDataException("Unexpected MEGA download length.");
            var encrypted = await ReadLimitedAsync(download.Content, checked((int)expectedSize), ct, progress, expectedSize);
            if (encrypted.LongLength != expectedSize) throw new InvalidDataException("The MEGA download is incomplete.");
            var hash = await MegaCrypto.DecryptAsync(encrypted, reference.Key, destination, ct);
            return new(name, size, hash);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested)
        {
            throw new TimeoutException("The MEGA download timed out. No game files were replaced.");
        }
    }

    private static void ValidateDownloadUri(Uri uri)
    {
        if (uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length != 0 ||
            !(uri.Host.EndsWith(".mega.co.nz", StringComparison.OrdinalIgnoreCase) || uri.Host.EndsWith(".mega.nz", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("MEGA returned an untrusted download host.");
    }

    private static async Task<byte[]> ReadLimitedAsync(HttpContent content, int limit, CancellationToken token,
        IProgress<TransferProgress>? progress = null, long total = 0)
    {
        using var input = await content.ReadAsStreamAsync(token);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(buffer, token)) > 0)
        {
            if (output.Length + read > limit) throw new InvalidDataException("MEGA response exceeds the expected size.");
            output.Write(buffer, 0, read);
            progress?.Report(new(output.Length, total));
        }
        return output.ToArray();
    }
}

public static class MegaCrypto
{
    private static byte[] AesKey(byte[] key)
    {
        if (key.Length != 32) throw new InvalidDataException("A MEGA file key must contain 32 bytes.");
        var result = new byte[16];
        for (var i = 0; i < 16; i++) result[i] = (byte)(key[i] ^ key[i + 16]);
        return result;
    }

    public static string AttributeName(string attributes, byte[] key)
    {
        var encrypted = MegaReference.Decode(attributes);
        if (encrypted.Length == 0 || encrypted.Length > 16384 || encrypted.Length % 16 != 0)
            throw new InvalidDataException("Invalid MEGA file attributes.");
        using var aes = Aes.Create(); aes.Key = AesKey(key);
        var plain = aes.DecryptCbc(encrypted, new byte[16], PaddingMode.None);
        var text = Encoding.UTF8.GetString(plain).TrimEnd('\0');
        if (!text.StartsWith("MEGA{", StringComparison.Ordinal)) throw new CryptographicException("Invalid MEGA attributes or file key.");
        using var json = JsonDocument.Parse(text[4..]);
        return json.RootElement.GetProperty("n").GetString() ?? throw new InvalidDataException("MEGA file name is missing.");
    }

    public static async Task<string> DecryptAsync(byte[] encrypted, byte[] key, Stream destination, CancellationToken token = default)
    {
        if (encrypted.Length == 0 || encrypted.Length > MegaClient.MaximumFileSize) throw new InvalidDataException("Invalid encrypted file size.");
        using var aes = Aes.Create(); aes.Key = AesKey(key);
        using var sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var nonce = key.AsSpan(16, 8).ToArray();
        var iv = nonce.Concat(nonce).ToArray();
        var counters = new byte[1024 * 1024];
        var keystream = new byte[counters.Length];
        var plain = new byte[counters.Length];
        var macBuffer = new byte[counters.Length];
        var fileMac = new byte[16];
        ulong counter = 0;
        var offset = 0;
        for (var chunk = 1; offset < encrypted.Length; chunk++)
        {
            token.ThrowIfCancellationRequested();
            var length = Math.Min(Math.Min(chunk, 8) * 131072, encrypted.Length - offset);
            var padded = (length + 15) / 16 * 16;
            for (var block = 0; block < padded; block += 16)
            {
                nonce.CopyTo(counters, block);
                BinaryPrimitives.WriteUInt64BigEndian(counters.AsSpan(block + 8, 8), counter++);
            }
            aes.EncryptEcb(counters.AsSpan(0, padded), keystream, PaddingMode.None);
            for (var i = 0; i < length; i++) plain[i] = (byte)(encrypted[offset + i] ^ keystream[i]);
            plain.AsSpan(length, padded - length).Clear();
            aes.EncryptCbc(plain.AsSpan(0, padded), iv, macBuffer, PaddingMode.None);
            for (var i = 0; i < 16; i++) fileMac[i] ^= macBuffer[padded - 16 + i];
            aes.EncryptEcb(fileMac, fileMac, PaddingMode.None);
            sha.AppendData(plain, 0, length);
            await destination.WriteAsync(plain.AsMemory(0, length), token);
            offset += length;
        }
        var mac = new byte[8];
        for (var i = 0; i < 4; i++)
        {
            mac[i] = (byte)(fileMac[i] ^ fileMac[i + 4]);
            mac[i + 4] = (byte)(fileMac[i + 8] ^ fileMac[i + 12]);
        }
        if (!CryptographicOperations.FixedTimeEquals(mac, key.AsSpan(24, 8)))
            throw new CryptographicException("MEGA file authentication failed. No patch will be installed.");
        return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
    }
}
