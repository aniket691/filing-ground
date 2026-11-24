namespace FinQuery.Domain.Errors;

/// <summary>
/// Expected, user-facing failure. The API maps these to HTTP. Unexpected
/// exceptions stay 500 and do not use this type.
/// </summary>
public class DomainError : Exception
{
    protected DomainError(string message) : base(message)
    {
    }

    /// <summary>JSON <c>error</c> field. Matches the Python class name.</summary>
    public string ErrorCode => GetType().Name;
}

public sealed class NotFoundError : DomainError
{
    public NotFoundError(string entity, int entityId)
        : this(entity, entityId.ToString())
    {
    }

    public NotFoundError(string entity, string entityId)
        : base($"{entity} with id {entityId} not found")
    {
        Entity = entity;
        EntityId = entityId;
    }

    public string Entity { get; }
    public string EntityId { get; }
}

public sealed class ConflictError : DomainError
{
    public ConflictError(string message) : base(message)
    {
    }
}

public sealed class ValidationError : DomainError
{
    public ValidationError(string message) : base(message)
    {
    }
}

public sealed class AuthenticationError : DomainError
{
    public AuthenticationError(string message) : base(message)
    {
    }
}
