using System.ComponentModel.DataAnnotations.Schema;

namespace Backend;

public class Company
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string LegalName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? Ticker { get; set; }
    public string? Exchange { get; set; }
    public string? Country { get; set; }
    public string? Industry { get; set; }
    public string? Sector { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    [ForeignKey("UserId")]
    public virtual User? User { get; set; }

    public virtual ICollection<Document> Documents { get; set; } = new List<Document>();
}