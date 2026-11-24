namespace FinQuery.Domain.Companies;

/// <summary>Row in the existing <c>companies</c> table. Owned by one user.</summary>
public sealed class Company
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string LegalName { get; set; } = "";

    public string? DisplayName { get; set; }

    public string? Ticker { get; set; }

    public string? Exchange { get; set; }

    public string? Country { get; set; }

    public string? Industry { get; set; }

    public string? Sector { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
