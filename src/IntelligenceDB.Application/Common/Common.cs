namespace IntelligenceDB.Application.Common;

/// <summary>Portal roles. Higher roles include everything lower roles can do.</summary>
public static class Roles
{
    public const string Admin = "Admin";
    public const string Dba = "DBA";
    public const string Operator = "Operator";
    public const string Viewer = "Viewer";

    public static readonly IReadOnlyList<string> All = [Admin, Dba, Operator, Viewer];
}

/// <summary>Authorization policy names used by the API.</summary>
public static class Policies
{
    /// <summary>Admin or DBA: add, edit and delete servers and groups.</summary>
    public const string ManageServers = "ManageServers";

    /// <summary>Admin only: users, settings, free-text T-SQL.</summary>
    public const string AdminOnly = "AdminOnly";
}

public abstract class AppException(string message) : Exception(message);

public sealed class NotFoundException(string message) : AppException(message);

public sealed class ConflictException(string message) : AppException(message);

public sealed class ForbiddenException(string message) : AppException(message);

/// <summary>Input failed validation. Errors are keyed by field name (camelCase, as the UI uses them).</summary>
public sealed class AppValidationException(IReadOnlyDictionary<string, string[]> errors)
    : AppException("One or more fields are invalid.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

/// <summary>Collects validation errors and throws them together.</summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _errors = new(StringComparer.Ordinal);

    public void Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list))
        {
            list = [];
            _errors[field] = list;
        }

        list.Add(message);
    }

    public void ThrowIfAny()
    {
        if (_errors.Count > 0)
        {
            throw new AppValidationException(_errors.ToDictionary(e => e.Key, e => e.Value.ToArray()));
        }
    }
}
