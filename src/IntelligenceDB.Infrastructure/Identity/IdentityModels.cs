using Microsoft.AspNetCore.Identity;

namespace IntelligenceDB.Infrastructure.Identity;

/// <summary>A portal user (not a SQL Server login).</summary>
public class AppUser : IdentityUser<Guid>
{
    public string DisplayName { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime? LastLoginUtc { get; set; }

    /// <summary>
    /// True: the user can see every server group. False: only the groups listed in
    /// <see cref="UserServerGroup"/>. Admins always see everything regardless of this flag.
    /// </summary>
    public bool AllServerGroups { get; set; } = true;
}

public class AppRole : IdentityRole<Guid>
{
    public AppRole()
    {
    }

    public AppRole(string roleName) : base(roleName)
    {
    }
}

/// <summary>Links a user to a server group they are allowed to see.</summary>
public class UserServerGroup
{
    public Guid UserId { get; set; }
    public int ServerGroupId { get; set; }
}

/// <summary>
/// A refresh token. Only the SHA-256 hash is stored; the raw value lives in an HttpOnly cookie.
/// Tokens are rotated on every use; re-use of an old token revokes the whole family.
/// </summary>
public class RefreshToken
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string TokenHash { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public DateTime ExpiresUtc { get; set; }
    public DateTime? RevokedUtc { get; set; }
    public Guid? ReplacedById { get; set; }
    public string? CreatedIp { get; set; }
}
