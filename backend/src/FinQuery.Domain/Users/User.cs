namespace FinQuery.Domain.Users;

/// <summary>
/// Row in the existing <c>users</c> table. The password column stores a bcrypt
/// hash only.
/// </summary>
public sealed class User
{
    public int Id { get; set; }

    public string Email { get; set; } = "";

    public string HashedPassword { get; set; } = "";

    public bool IsActive { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
