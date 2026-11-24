using System.Security.Cryptography;

namespace FinQuery.Application.Documents;

public interface IDocumentFileStore
{
    /// <summary>Writes the bytes and returns the storage key. Does not delete existing files.</summary>
    string Store(int? companyId, string filename, byte[] data);

    byte[] Read(string key);
}

public static class DocumentStorageKeys
{
    public static string Build(int? companyId, string filename, byte[] data)
    {
        var safeName = Sanitize(filename);
        var digest = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var prefix = companyId is null ? "unassigned" : $"companies/{companyId}";
        return $"{prefix}/{digest[..12]}/{safeName}";
    }

    public static string Sanitize(string name)
    {
        var cleaned = name.Replace('\\', '/').Split('/')[^1].Trim();
        cleaned = string.Join(' ', cleaned.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length == 0 || cleaned is "." or "..")
        {
            throw new InvalidOperationException($"Invalid filename: {name}");
        }

        return cleaned;
    }
}
