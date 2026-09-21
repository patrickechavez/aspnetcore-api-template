using ApiTemplate.Common;
using ApiTemplate.Common.Mapping;
using ApiTemplate.DTOs.Users;
using ApiTemplate.Models;
using ApiTemplate.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApiTemplate.Services;

public class UserService : IUserService
{
    private const int MaxPageSize = 100;

    private readonly IUserRepository _users;
    private readonly ICurrentUser _currentUser;
    private readonly IPasswordHasher<User> _passwordHasher;

    public UserService(
        IUserRepository users,
        ICurrentUser currentUser,
        IPasswordHasher<User> passwordHasher)
    {
        _users = users;
        _currentUser = currentUser;
        _passwordHasher = passwordHasher;
    }

    public async Task<Result<PagedResult<UserDto>>> GetPagedAsync(
        int page, int pageSize, CancellationToken cancellationToken = default)
    {
        // Duplicates the controller's [Authorize(Roles = "Admin")] on purpose. The attribute only
        // guards the HTTP route; this guards the method, so a background job or a second controller
        // calling IUserService directly cannot list every user.
        if (!_currentUser.IsAdmin)
        {
            return Result<PagedResult<UserDto>>.Forbidden("Only an administrator can list users.");
        }

        if (page < 1)
        {
            return Result<PagedResult<UserDto>>.Validation("Page must be 1 or greater.");
        }

        if (pageSize is < 1 or > MaxPageSize)
        {
            return Result<PagedResult<UserDto>>.Validation($"PageSize must be between 1 and {MaxPageSize}.");
        }

        // The repository computes Skip((page - 1) * pageSize) in int arithmetic. Without this
        // guard a large-but-valid page overflows to a negative OFFSET, which Postgres rejects
        // as an unhandled 500 instead of a clean 400.
        if ((long)(page - 1) * pageSize > int.MaxValue)
        {
            return Result<PagedResult<UserDto>>.Validation("Page is out of range.");
        }

        var (items, totalCount) = await _users.GetPagedAsync(page, pageSize, cancellationToken);

        return Result<PagedResult<UserDto>>.Success(new PagedResult<UserDto>
        {
            Items = items.Select(u => u.ToDto()).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalCount = totalCount
        });
    }

    public async Task<Result<UserDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Authorization is evaluated before the lookup, so a non-owner receives 403
        // whether or not the id exists. Returning 404 here would leak which ids are real.
        if (!CanActOn(id))
        {
            return Result<UserDto>.Forbidden("You can only access your own account.");
        }

        var user = await _users.GetByIdAsync(id, cancellationToken);

        return user is null
            ? Result<UserDto>.NotFound($"No user with id {id}.")
            : Result<UserDto>.Success(user.ToDto());
    }

    public async Task<Result<UserDto>> GetCurrentAsync(CancellationToken cancellationToken = default)
    {
        if (_currentUser.Id is not { } callerId)
        {
            return Result<UserDto>.Unauthorized("Not authenticated.");
        }

        var user = await _users.GetByIdAsync(callerId, cancellationToken);

        return user is null
            ? Result<UserDto>.Unauthorized("Not authenticated.")
            : Result<UserDto>.Success(user.ToDto());
    }

    public async Task<Result<UserDto>> CreateAsync(
        CreateUserRequest request, CancellationToken cancellationToken = default)
    {
        // Guards the method, not just the route. Without this, any caller reaching IUserService
        // directly could create an administrator — CreateUserRequest carries a Role.
        if (!_currentUser.IsAdmin)
        {
            return Result<UserDto>.Forbidden("Only an administrator can create users.");
        }

        var email = request.Email.Trim().ToLowerInvariant();

        if (await _users.EmailExistsAsync(email, cancellationToken))
        {
            return Result<UserDto>.Conflict("Email is already registered.");
        }

        if (!Enum.TryParse<UserRole>(request.Role, out var role))
        {
            return Result<UserDto>.Validation("Role must be either 'Admin' or 'User'.");
        }

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName ?? email.Split('@')[0],
            Role = role
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        try
        {
            await _users.AddAsync(user, cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two concurrent creates can both pass the check above; the unique index is the arbiter.
            return Result<UserDto>.Conflict("Email is already registered.");
        }

        return Result<UserDto>.Success(user.ToDto());
    }

    public async Task<Result<UserDto>> UpdateAsync(
        Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default)
    {
        if (!CanActOn(id))
        {
            return Result<UserDto>.Forbidden("You can only modify your own account.");
        }

        // Only an administrator may change a role. A user updating their own record
        // cannot escalate their privileges.
        if (request.Role is not null && !_currentUser.IsAdmin)
        {
            return Result<UserDto>.Forbidden("Only an administrator can change a role.");
        }

        // An admin demoting themselves would defeat the self-delete guard below and could leave
        // the system with no administrator at all.
        if (request.Role is not null
            && _currentUser.Id == id
            && _currentUser.IsAdmin
            && !string.Equals(request.Role, nameof(UserRole.Admin), StringComparison.Ordinal))
        {
            return Result<UserDto>.Forbidden("You cannot remove your own administrator role.");
        }

        var user = await _users.GetByIdAsync(id, cancellationToken);

        if (user is null)
        {
            return Result<UserDto>.NotFound($"No user with id {id}.");
        }

        // Omitted fields mean "leave unchanged". Assigning unconditionally would let a
        // role-only update silently wipe the display name that CreateAsync guarantees exists.
        if (request.DisplayName is not null)
        {
            user.DisplayName = request.DisplayName;
        }

        if (request.Role is not null)
        {
            if (!Enum.TryParse<UserRole>(request.Role, out var role))
            {
                return Result<UserDto>.Validation("Role must be either 'Admin' or 'User'.");
            }

            user.Role = role;
        }

        user.UpdatedAt = DateTime.UtcNow;

        await _users.UpdateAsync(user, cancellationToken);

        return Result<UserDto>.Success(user.ToDto());
    }

    public async Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        // Guards the method, not just the route. Checked before the self-delete guard so the
        // answer to "may you delete at all?" precedes "may you delete this particular account?".
        if (!_currentUser.IsAdmin)
        {
            return Result.Forbidden("Only an administrator can delete users.");
        }

        // Prevents the last administrator from locking everyone out of user management.
        if (_currentUser.Id == id)
        {
            return Result.Forbidden("You cannot delete your own account.");
        }

        var user = await _users.GetByIdAsync(id, cancellationToken);

        if (user is null)
        {
            return Result.NotFound($"No user with id {id}.");
        }

        await _users.DeleteAsync(user, cancellationToken);

        return Result.Success();
    }

    private bool CanActOn(Guid resourceUserId) =>
        _currentUser.IsAdmin || _currentUser.Id == resourceUserId;
}
