using IntelligenceDB.Application.Abstractions;

namespace IntelligenceDB.Infrastructure.Security;

/// <summary>The identity of background services (Worker). Sees every server; audit rows show "system".</summary>
public sealed class SystemCurrentUser(string name) : ICurrentUser
{
    public string? UserId => null;
    public string? UserName => name;
    public string? ClientIp => null;
    public bool IsInRole(string role) => false;
    public Task<AccessScope> GetScopeAsync(CancellationToken cancellationToken) => Task.FromResult(AccessScope.Everything);
}
