namespace Bcra.Shared.Domain.Errors;

public enum ErrorType
{
    Validation,
    NotFound,
    Conflict,
    Unavailable,
    Failure
}

public sealed record Error
{
    public string Code { get; }
    public string Description { get; }
    public ErrorType Type { get; }

    private Error(string code, string description, ErrorType type)
    {
        Code = code;
        Description = description;
        Type = type;
    }

    public static Error None { get; } = new("none", "No error.", ErrorType.Failure);

    public static Error Validation(string code, string description) => new(code, description, ErrorType.Validation);
    public static Error NotFound(string code, string description) => new(code, description, ErrorType.NotFound);
    public static Error Conflict(string code, string description) => new(code, description, ErrorType.Conflict);
    public static Error Unavailable(string code, string description) => new(code, description, ErrorType.Unavailable);
    public static Error Failure(string code, string description) => new(code, description, ErrorType.Failure);

    public override string ToString() => $"{Code}: {Description}";
}
