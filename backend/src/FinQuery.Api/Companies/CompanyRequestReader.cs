using System.Text.Json;
using System.Text.RegularExpressions;
using FinQuery.Api.Auth;
using FinQuery.Application.Companies;

namespace FinQuery.Api.Companies;

public static class CompanyRequestReader
{
    private static readonly Regex TickerPattern = new("^[A-Za-z0-9.\\-]{1,32}$", RegexOptions.CultureInvariant);

    public static CompanyWrite ReadCreate(JsonElement body)
    {
        var fields = ReadObject(body, allowed: ["legal_name", "display_name", "ticker", "exchange", "country", "industry", "sector"]);
        if (!fields.TryGetValue("legal_name", out var legalNameNode) || legalNameNode.ValueKind != JsonValueKind.String)
        {
            throw new BodyValidationException("Field required", "legal_name");
        }

        var legalName = legalNameNode.GetString() ?? "";
        if (legalName.Length is < 1 or > 255)
        {
            throw new BodyValidationException("String should have at most 255 characters", "legal_name");
        }

        return new CompanyWrite(
            legalName,
            OptionalString(fields, "display_name", 255),
            OptionalTicker(fields),
            OptionalString(fields, "exchange", 64),
            OptionalCountry(fields),
            OptionalString(fields, "industry", 128),
            OptionalString(fields, "sector", 128));
    }

    public static CompanyPatch ReadPatch(JsonElement body)
    {
        var fields = ReadObject(body, allowed: ["display_name", "country", "industry", "sector"]);
        return new CompanyPatch
        {
            DisplayNameSet = fields.ContainsKey("display_name"),
            DisplayName = fields.ContainsKey("display_name") ? OptionalString(fields, "display_name", 255) : null,
            CountrySet = fields.ContainsKey("country"),
            Country = fields.ContainsKey("country") ? OptionalCountry(fields) : null,
            IndustrySet = fields.ContainsKey("industry"),
            Industry = fields.ContainsKey("industry") ? OptionalString(fields, "industry", 128) : null,
            SectorSet = fields.ContainsKey("sector"),
            Sector = fields.ContainsKey("sector") ? OptionalString(fields, "sector", 128) : null
        };
    }

    public static int ParseBound(string? raw, int fallback, int min, int max, string name)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return fallback;
        }

        if (!int.TryParse(raw, out var value) || value < min || value > max)
        {
            throw new BodyValidationException($"Input should be greater than or equal to {min}", name);
        }

        return value;
    }

    private static Dictionary<string, JsonElement> ReadObject(JsonElement body, string[] allowed)
    {
        if (body.ValueKind != JsonValueKind.Object)
        {
            throw new BodyValidationException("Request body must be an object.", "body");
        }

        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var property in body.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
            {
                throw new BodyValidationException("Extra inputs are not permitted", property.Name);
            }

            fields[property.Name] = property.Value;
        }

        return fields;
    }

    private static string? OptionalString(Dictionary<string, JsonElement> fields, string name, int maxLength)
    {
        if (!fields.TryGetValue(name, out var node) || node.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (node.ValueKind != JsonValueKind.String)
        {
            throw new BodyValidationException("Input should be a valid string", name);
        }

        var value = node.GetString();
        if (value is not null && value.Length > maxLength)
        {
            throw new BodyValidationException($"String should have at most {maxLength} characters", name);
        }

        return value;
    }

    private static string? OptionalTicker(Dictionary<string, JsonElement> fields)
    {
        var ticker = OptionalString(fields, "ticker", 32);
        if (ticker is null)
        {
            return null;
        }

        if (!TickerPattern.IsMatch(ticker))
        {
            throw new BodyValidationException("String should match pattern '^[A-Za-z0-9.\\-]{1,32}$'", "ticker");
        }

        return ticker;
    }

    private static string? OptionalCountry(Dictionary<string, JsonElement> fields)
    {
        if (!fields.TryGetValue("country", out var node) || node.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (node.ValueKind != JsonValueKind.String)
        {
            throw new BodyValidationException("Input should be a valid string", "country");
        }

        var value = node.GetString() ?? "";
        if (value.Length != 2)
        {
            throw new BodyValidationException("String should have at most 2 characters", "country");
        }

        return value;
    }
}
