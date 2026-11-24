using FinQuery.Application.Documents;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace FinQuery.Api.Configuration;

public static class StorageSettingsBinder
{
    public static StorageSettings Bind(IConfiguration configuration, IHostEnvironment environment)
    {
        if (environment.IsEnvironment("Testing"))
        {
            var directory = Path.Combine(Path.GetTempPath(), "finquery-upload-tests", Guid.NewGuid().ToString("n"));
            return new StorageSettings("local", directory);
        }

        var backend = configuration["STORAGE_BACKEND"];
        if (string.IsNullOrWhiteSpace(backend))
        {
            backend = "local";
        }

        var path = configuration["STORAGE_PATH"];
        if (string.IsNullOrWhiteSpace(path))
        {
            path = Path.GetFullPath(Path.Combine(environment.ContentRootPath, "..", "..", "..", "backend", "storage_data"));
        }

        return new StorageSettings(backend.Trim().ToLowerInvariant(), path);
    }
}
