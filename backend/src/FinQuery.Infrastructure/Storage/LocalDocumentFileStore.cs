using FinQuery.Application.Documents;

namespace FinQuery.Infrastructure.Storage;

/// <summary>
/// Writes files under the configured root. Keys stay inside that root.
/// Existing files are overwritten in place, never deleted by this type.
/// </summary>
public sealed class LocalDocumentFileStore : IDocumentFileStore
{
    private readonly string _root;

    public LocalDocumentFileStore(StorageSettings settings)
    {
        if (!string.Equals(settings.Backend, "local", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Unsupported STORAGE_BACKEND '{settings.Backend}'. Available: local");
        }

        _root = Path.GetFullPath(settings.Path);
        Directory.CreateDirectory(_root);
    }

    public string Store(int? companyId, string filename, byte[] data)
    {
        var key = DocumentStorageKeys.Build(companyId, filename, data);
        var path = Resolve(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, data);
        return key;
    }

    public byte[] Read(string key)
    {
        var path = Resolve(key);
        if (!File.Exists(path))
        {
            throw new FileNotFoundException($"Stored file was not found: {key}");
        }

        return File.ReadAllBytes(path);
    }

    private string Resolve(string key)
    {
        var normalized = key.Replace('\\', '/');
        if (Path.IsPathRooted(normalized) || normalized.Split('/').Any(part => part is "" or "." or ".."))
        {
            throw new InvalidOperationException($"Invalid storage key: {key}");
        }

        var target = Path.GetFullPath(Path.Combine(_root, normalized.Replace('/', Path.DirectorySeparatorChar)));
        var rootWithSeparator = _root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!target.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(target, _root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Storage key escapes root: {key}");
        }

        return target;
    }
}
