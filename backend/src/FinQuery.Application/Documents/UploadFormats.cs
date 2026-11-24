namespace FinQuery.Application.Documents;

public static class DocumentTypes
{
    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.Ordinal)
    {
        "10-K",
        "10-Q",
        "8-K",
        "Annual Report",
        "Quarterly Report",
        "Investor Presentation",
        "Earnings Release",
        "Financial Statement",
        "Research Report",
        "Other"
    };
}

public static class UploadFormats
{
    public const int MaxBytes = 25 * 1024 * 1024;

    public static readonly string[] Extensions = ["htm", "html", "md", "pdf", "text", "txt"];

    /// <summary>Matches pathlib <c>Path.suffix</c>, then lower-cases and drops the dot.</summary>
    public static string ExtensionOf(string filename)
    {
        var name = filename.Replace('\\', '/');
        var slash = name.LastIndexOf('/');
        if (slash >= 0)
        {
            name = name[(slash + 1)..];
        }

        var dot = name.LastIndexOf('.');
        if (dot <= 0)
        {
            return "";
        }

        return name[dot..].ToLowerInvariant().TrimStart('.');
    }

    public static void EnsureSupported(string filename)
    {
        var extension = ExtensionOf(filename);
        if (!Extensions.Contains(extension))
        {
            var supported = string.Join(", ", Extensions.Select(item => $"'{item}'"));
            throw new FinQuery.Domain.Errors.ValidationError(
                $"Unsupported file format '{extension}'. Supported: [{supported}]");
        }
    }
}
