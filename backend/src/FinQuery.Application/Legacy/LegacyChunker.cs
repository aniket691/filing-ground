using System.Security.Cryptography;
using System.Text;

namespace FinQuery.Application.Legacy;

public static class LegacyChunker
{
    private static readonly string[] Separators = ["\n\n", "\n", ". ", " ", ""];

    public static IReadOnlyList<LegacyChunk> Chunk(IReadOnlyList<(string Text, Dictionary<string, object> Metadata)> documents, int size, int overlap)
    {
        var pieces = new List<LegacyChunk>();
        var index = 0;
        foreach (var (text, metadata) in documents)
        {
            foreach (var piece in Split(text, Separators, size, overlap))
            {
                var meta = new Dictionary<string, object>(metadata)
                {
                    ["chunk_index"] = index,
                    ["chunk_size"] = piece.Length
                };
                pieces.Add(new LegacyChunk(piece, meta));
                index++;
            }
        }

        return pieces;
    }

    public static string ChunkId(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text));
        var bucket = BitConverter.ToUInt32(hash, 0) % 100_000_000;
        return $"chunk_{bucket}";
    }

    private static List<string> Split(string text, IReadOnlyList<string> separators, int size, int overlap)
    {
        var separator = separators[^1];
        var rest = Array.Empty<string>();
        for (var i = 0; i < separators.Count; i++)
        {
            var candidate = separators[i];
            if (candidate.Length == 0)
            {
                separator = candidate;
                rest = [];
                break;
            }

            if (text.Contains(candidate, StringComparison.Ordinal))
            {
                separator = candidate;
                rest = separators.Skip(i + 1).ToArray();
                break;
            }
        }

        var parts = separator.Length == 0
            ? text.Select(character => character.ToString()).ToList()
            : text.Split(separator).ToList();
        var merged = new List<string>();
        var current = new List<string>();
        var length = 0;
        foreach (var part in parts)
        {
            var next = part.Length + (current.Count > 0 ? separator.Length : 0);
            if (current.Count > 0 && length + next > size)
            {
                var joined = string.Join(separator, current);
                if (joined.Length > size && rest.Length > 0)
                {
                    merged.AddRange(Split(joined, rest, size, overlap));
                }
                else if (joined.Length > 0)
                {
                    merged.Add(joined);
                }

                while (current.Count > 1 && length > overlap)
                {
                    length -= current[0].Length + separator.Length;
                    current.RemoveAt(0);
                }

                if (current.Count == 1 && current[0].Length > overlap)
                {
                    current.Clear();
                    length = 0;
                }
            }

            current.Add(part);
            length += part.Length + (current.Count > 1 ? separator.Length : 0);
        }

        if (current.Count > 0)
        {
            var joined = string.Join(separator, current);
            if (joined.Length > size && rest.Length > 0)
            {
                merged.AddRange(Split(joined, rest, size, overlap));
            }
            else if (joined.Length > 0)
            {
                merged.Add(joined);
            }
        }

        return merged;
    }
}

public static class LegacyLoader
{
    public static List<(string Text, Dictionary<string, object> Metadata)> LoadForIngest(IReadOnlyList<string>? filePaths, LegacyPaths paths)
    {
        if (filePaths is { Count: > 0 })
        {
            var selected = new List<(string Text, Dictionary<string, object> Metadata)>();
            foreach (var path in filePaths)
            {
                selected.AddRange(path.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase)
                    ? LoadPdf(path)
                    : LoadText(path));
            }

            return selected;
        }

        var raw = LoadDirectory(paths.RawDocsPath);
        return raw.Count > 0 ? raw : LoadDirectory(paths.SampleDocsPath);
    }

    public static List<(string Text, Dictionary<string, object> Metadata)> LoadDirectory(string directory)
    {
        if (!Directory.Exists(directory))
        {
            return [];
        }

        var documents = new List<(string Text, Dictionary<string, object> Metadata)>();
        foreach (var path in Directory.GetFiles(directory).OrderBy(path => Path.GetFileName(path), StringComparer.Ordinal))
        {
            var name = Path.GetFileName(path);
            if (name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                documents.AddRange(LoadPdf(path));
            }
            else if (name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase))
            {
                documents.AddRange(LoadText(path));
            }
        }

        return documents;
    }

    public static List<(string Text, Dictionary<string, object> Metadata)> LoadText(string path)
    {
        var content = File.ReadAllText(path, Encoding.UTF8);
        return [(content, new Dictionary<string, object> { ["source"] = Path.GetFileName(path), ["file_type"] = "text" })];
    }

    public static List<(string Text, Dictionary<string, object> Metadata)> LoadPdf(string path)
    {
        using var document = UglyToad.PdfPig.PdfDocument.Open(path);
        var name = Path.GetFileName(path);
        return document.GetPages()
            .Select(page => (page.Text ?? "", new Dictionary<string, object> { ["source"] = name, ["file_type"] = "pdf" }))
            .ToList();
    }
}
