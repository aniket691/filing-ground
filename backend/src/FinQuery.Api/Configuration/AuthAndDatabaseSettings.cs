using FinQuery.Application.Auth;

namespace FinQuery.Api.Configuration;

public sealed class DatabaseSelection
{
    public string? MySqlConnectionString { get; init; }

    public string? SqliteConnectionString { get; init; }
}

public static class AuthAndDatabaseSettings
{
    public static AuthSettings BindAuth(IConfiguration configuration)
    {
        var minutes = 30;
        if (int.TryParse(configuration["ACCESS_TOKEN_EXPIRE_MINUTES"], out var parsedMinutes) && parsedMinutes > 0)
        {
            minutes = parsedMinutes;
        }

        var rounds = 12;
        if (int.TryParse(configuration["AUTH_BCRYPT_ROUNDS"], out var parsedRounds) && parsedRounds > 0)
        {
            rounds = parsedRounds;
        }

        return new AuthSettings
        {
            SecretKey = configuration["AUTH_SECRET_KEY"] ?? "",
            Algorithm = string.IsNullOrWhiteSpace(configuration["AUTH_ALGORITHM"])
                ? "HS256"
                : configuration["AUTH_ALGORITHM"]!.Trim(),
            AccessTokenExpireMinutes = minutes,
            BcryptRounds = rounds
        };
    }

    /// <summary>
    /// Reads <c>DATABASE_URL</c> (<c>mysql+pymysql://</c> or <c>sqlite:///</c>) or
    /// <c>MYSQL_HOST</c> plus the Compose user, password, and database names.
    /// Returns an unconfigured selection when those are absent. Never builds a
    /// connection to a guessed host.
    /// </summary>
    public static DatabaseSelection BindDatabase(IConfiguration configuration)
    {
        var url = configuration["DATABASE_URL"];
        if (!string.IsNullOrWhiteSpace(url))
        {
            if (url.StartsWith("sqlite:", StringComparison.OrdinalIgnoreCase))
            {
                return new DatabaseSelection { SqliteConnectionString = SqliteConnection(url) };
            }

            var mysql = MySqlConnection(url);
            if (mysql is not null)
            {
                return new DatabaseSelection { MySqlConnectionString = mysql };
            }
        }

        var host = configuration["MYSQL_HOST"];
        var database = configuration["MYSQL_DATABASE"];
        var user = configuration["MYSQL_USER"];
        var password = configuration["MYSQL_PASSWORD"];
        if (string.IsNullOrWhiteSpace(host) || string.IsNullOrWhiteSpace(database) || string.IsNullOrWhiteSpace(user))
        {
            return new DatabaseSelection();
        }

        var port = configuration["MYSQL_PORT"];
        if (string.IsNullOrWhiteSpace(port))
        {
            port = "3306";
        }

        return new DatabaseSelection
        {
            MySqlConnectionString = $"Server={host};Port={port};Database={database};User={user};Password={password};"
        };
    }

    private static string? MySqlConnection(string url)
    {
        var schemeEnd = url.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd < 0)
        {
            return null;
        }

        var scheme = url[..schemeEnd];
        if (!scheme.StartsWith("mysql", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!Uri.TryCreate(url.Replace(scheme + "://", "http://", StringComparison.Ordinal), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var userInfo = uri.UserInfo.Split(':', 2);
        var user = Uri.UnescapeDataString(userInfo[0]);
        var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
        var database = uri.AbsolutePath.Trim('/');
        var port = uri.Port > 0 ? uri.Port : 3306;
        return $"Server={uri.Host};Port={port};Database={database};User={user};Password={password};";
    }

    private static string SqliteConnection(string url)
    {
        var path = url["sqlite:".Length..].TrimStart('/');
        if (url.StartsWith("sqlite:///", StringComparison.OrdinalIgnoreCase))
        {
            path = url["sqlite:///".Length..];
        }

        return $"Data Source={path}";
    }
}
