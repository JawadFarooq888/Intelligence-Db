using System.ComponentModel.DataAnnotations;
using IntelligenceDB.Application.Abstractions;
using IntelligenceDB.Application.Common;
using IntelligenceDB.Infrastructure.Identity;
using IntelligenceDB.Infrastructure.Repository;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace IntelligenceDB.Api.Controllers;

public sealed record UserAdminDto(
    Guid Id,
    string Email,
    string DisplayName,
    string Role,
    bool AllServerGroups,
    IReadOnlyList<int> GroupIds,
    bool TwoFactorEnabled,
    bool IsLockedOut,
    DateTime CreatedUtc,
    DateTime? LastLoginUtc);

public sealed class CreateUserRequest
{
    [Required, EmailAddress, MaxLength(256)]
    public string Email { get; init; } = string.Empty;

    [Required, MaxLength(128)]
    public string DisplayName { get; init; } = string.Empty;

    [Required, MaxLength(256)]
    public string Password { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = Roles.Viewer;

    public bool AllServerGroups { get; init; } = true;
    public List<int> GroupIds { get; init; } = [];
}

public sealed class UpdateUserRequest
{
    [Required, MaxLength(128)]
    public string DisplayName { get; init; } = string.Empty;

    [Required]
    public string Role { get; init; } = Roles.Viewer;

    public bool AllServerGroups { get; init; } = true;
    public List<int> GroupIds { get; init; } = [];

    /// <summary>Clears a lockout caused by failed sign-ins.</summary>
    public bool Unlock { get; init; }

    /// <summary>Optional: set a new password (the user's other sessions are signed out).</summary>
    [MaxLength(256)]
    public string? NewPassword { get; init; }
}

/// <summary>Portal user management (Admin only). Each user has exactly one role.</summary>
[ApiController]
[Route("api/users")]
[Authorize(Policy = Policies.AdminOnly)]
public sealed class UsersController(
    UserManager<AppUser> users,
    RepoDbContext db,
    IAuditWriter audit,
    TimeProvider clock) : ControllerBase
{
    [HttpGet]
    public async Task<IReadOnlyList<UserAdminDto>> List(CancellationToken ct)
    {
        var all = await db.Users.AsNoTracking().OrderBy(u => u.Email).ToListAsync(ct);
        var roles = await (from ur in db.UserRoles
                           join r in db.Roles on ur.RoleId equals r.Id
                           select new { ur.UserId, r.Name }).ToListAsync(ct);
        var groups = await db.UserServerGroups.AsNoTracking().ToListAsync(ct);
        var now = clock.GetUtcNow();

        return all.Select(u => new UserAdminDto(
            u.Id,
            u.Email ?? string.Empty,
            u.DisplayName,
            roles.FirstOrDefault(r => r.UserId == u.Id)?.Name ?? string.Empty,
            u.AllServerGroups,
            groups.Where(g => g.UserId == u.Id).Select(g => g.ServerGroupId).ToList(),
            u.TwoFactorEnabled,
            u.LockoutEnd is { } end && end > now,
            u.CreatedUtc,
            u.LastLoginUtc)).ToList();
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateUserRequest request, CancellationToken ct)
    {
        if (!Roles.All.Contains(request.Role)) return RoleProblem();
        if (!await GroupsExistAsync(request.GroupIds, ct)) return GroupProblem();

        var user = new AppUser
        {
            UserName = request.Email.Trim(),
            Email = request.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = request.DisplayName.Trim(),
            CreatedUtc = clock.GetUtcNow().UtcDateTime,
            AllServerGroups = request.AllServerGroups,
        };

        var result = await users.CreateAsync(user, request.Password);
        if (!result.Succeeded) return IdentityProblem(result);

        await users.AddToRoleAsync(user, request.Role);
        await SetGroupsAsync(user.Id, request.AllServerGroups ? [] : request.GroupIds, ct);
        await audit.WriteAsync(new AuditRecord("User.Create", Details: new { user.Id, user.Email, request.Role, request.AllServerGroups, request.GroupIds }), ct);
        return NoContent();
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateUserRequest request, CancellationToken ct)
    {
        if (!Roles.All.Contains(request.Role)) return RoleProblem();
        if (!await GroupsExistAsync(request.GroupIds, ct)) return GroupProblem();

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        var currentRoles = await users.GetRolesAsync(user);
        if (currentRoles.Contains(Roles.Admin) && request.Role != Roles.Admin && await IsLastAdminAsync())
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "At least one Admin must remain.");
        }

        user.DisplayName = request.DisplayName.Trim();
        user.AllServerGroups = request.AllServerGroups;
        var update = await users.UpdateAsync(user);
        if (!update.Succeeded) return IdentityProblem(update);

        if (!currentRoles.SequenceEqual([request.Role]))
        {
            await users.RemoveFromRolesAsync(user, currentRoles);
            await users.AddToRoleAsync(user, request.Role);
        }

        if (request.Unlock)
        {
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
        }

        if (!string.IsNullOrEmpty(request.NewPassword))
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var reset = await users.ResetPasswordAsync(user, token, request.NewPassword);
            if (!reset.Succeeded) return IdentityProblem(reset);
            await SignOutEverywhereAsync(user.Id, ct);
        }

        await SetGroupsAsync(user.Id, request.AllServerGroups ? [] : request.GroupIds, ct);
        await audit.WriteAsync(new AuditRecord("User.Update", Details: new
        {
            user.Id,
            user.Email,
            request.Role,
            request.AllServerGroups,
            request.GroupIds,
            request.Unlock,
            passwordReset = !string.IsNullOrEmpty(request.NewPassword),
        }), ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        if (User.FindFirst("sub")?.Value == id.ToString())
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "You cannot delete your own account.");
        }

        var user = await users.FindByIdAsync(id.ToString());
        if (user is null) return NotFound();

        if (await users.IsInRoleAsync(user, Roles.Admin) && await IsLastAdminAsync())
        {
            return Problem(statusCode: StatusCodes.Status409Conflict, title: "At least one Admin must remain.");
        }

        var result = await users.DeleteAsync(user);
        if (!result.Succeeded) return IdentityProblem(result);

        await audit.WriteAsync(new AuditRecord("User.Delete", Details: new { user.Id, user.Email }), ct);
        return NoContent();
    }

    private async Task<bool> IsLastAdminAsync() => (await users.GetUsersInRoleAsync(Roles.Admin)).Count <= 1;

    private async Task<bool> GroupsExistAsync(List<int> ids, CancellationToken ct)
    {
        if (ids.Count == 0) return true;
        var distinct = ids.Distinct().ToList();
        return await db.ServerGroups.CountAsync(g => distinct.Contains(g.Id), ct) == distinct.Count;
    }

    private async Task SetGroupsAsync(Guid userId, List<int> groupIds, CancellationToken ct)
    {
        await db.UserServerGroups.Where(x => x.UserId == userId).ExecuteDeleteAsync(ct);
        foreach (var gid in groupIds.Distinct())
        {
            db.UserServerGroups.Add(new UserServerGroup { UserId = userId, ServerGroupId = gid });
        }

        await db.SaveChangesAsync(ct);
    }

    private async Task SignOutEverywhereAsync(Guid userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.RefreshTokens
            .Where(t => t.UserId == userId && t.RevokedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedUtc, (DateTime?)now), ct);
    }

    private ObjectResult RoleProblem() =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: $"Role must be one of: {string.Join(", ", Roles.All)}.");

    private ObjectResult GroupProblem() =>
        Problem(statusCode: StatusCodes.Status400BadRequest, title: "One or more server groups do not exist.");

    private BadRequestObjectResult IdentityProblem(IdentityResult result) =>
        BadRequest(new ValidationProblemDetails(new Dictionary<string, string[]>
        {
            ["identity"] = result.Errors.Select(e => e.Description).ToArray(),
        })
        {
            Title = string.Join(" ", result.Errors.Select(e => e.Description)),
            Status = StatusCodes.Status400BadRequest,
        });
}
