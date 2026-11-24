namespace FinQuery.Api.Configuration;

/// <summary>
/// Loads <c>.env</c> the way the Python app does: <c>backend/.env</c> first,
/// then the repo-root <c>.env</c>, and the root file wins. Values override
/// environment variables already loaded by the host.
/// </summary>
public static class DotEnvLoader
{
    public static void Apply(ConfigurationManager configuration)
    {
        var root = FindRepoRoot();
        if (root is null)
        {
            return;
        }

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        ReadFile(Path.Combine(root, "backend", ".env"), values);
        ReadFile(Path.Combine(root, ".env"), values);
        if (values.Count == 0)
        {
            return;
        }

        configuration.AddInMemoryCollection(values);
    }

    private static string? FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var marker = Path.Combine(dir.FullName, ".env.example");
            var backend = Path.Combine(dir.FullName, "backend");
            if (File.Exists(marker) && Directory.Exists(backend))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        return null;
    }

    private static void ReadFile(string path, Dictionary<string, string?> values)
    {
        if (!File.Exists(path))
        {
            return;
        }

        foreach (var rawLine in File.ReadLines(path))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
            {
                continue;
            }

            if (line.StartsWith("export ", StringComparison.Ordinal))
            {
                line = line["export ".Length..].Trim();
            }

            var split = line.IndexOf('=');
            if (split <= 0)
            {
                continue;
            }

            var key = line[..split].Trim();
            var value = line[(split + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = value[1..^1];
            }
            else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                value = value[1..^1];
            }

            values[key] = value;
        }
    }
}
