using ApiTemplate.Common;
using ApiTemplate.Common.Mapping;
using ApiTemplate.DTOs.Auth;
using ApiTemplate.Models;
using ApiTemplate.Repositories;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ApiTemplate.Services;

public class AuthService : IAuthService
{
    private readonly IUserRepository _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenService _tokenService;
    private readonly IPasswordHasher<User> _passwordHasher;
    private readonly ILogger<AuthService> _logger;

    // Verified against on the unknown-email path so it costs the same PBKDF2 work as a wrong
    // password. Without this, response timing reveals whether an account exists.
    private static readonly string DummyPasswordHash =
        new PasswordHasher<User>().HashPassword(new User(), "not-a-real-password");

    public AuthService(
        IUserRepository users,
        IRefreshTokenRepository refreshTokens,
        ITokenService tokenService,
        IPasswordHasher<User> passwordHasher,
        ILogger<AuthService> logger)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _tokenService = tokenService;
        _passwordHasher = passwordHasher;
        _logger = logger;
    }

    public async Task<Result<AuthResponse>> RegisterAsync(
        RegisterRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();

        if (await _users.EmailExistsAsync(email, cancellationToken))
        {
            _logger.LogDebug("Registration rejected: {Email} already exists", email);
            return Result<AuthResponse>.Conflict("Email is already registered.");
        }

        var user = new User
        {
            Email = email,
            DisplayName = request.DisplayName ?? email.Split('@')[0],
            Role = UserRole.User
        };

        user.PasswordHash = _passwordHasher.HashPassword(user, request.Password);

        try
        {
            await _users.AddAsync(user, cancellationToken);
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Two concurrent registrations can both pass the check above; the unique index
            // is the authoritative arbiter.
            _logger.LogDebug("Registration rejected: {Email} lost an insert race", email);
            return Result<AuthResponse>.Conflict("Email is already registered.");
        }

        return Result<AuthResponse>.Success(await IssueTokensAsync(user, cancellationToken));
    }

    public async Task<Result<AuthResponse>> LoginAsync(
        LoginRequest request, CancellationToken cancellationToken = default)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await _users.GetByEmailAsync(email, cancellationToken);

        if (user is null)
        {
            // Verified against a dummy hash so this path costs the same PBKDF2 work as a
            // wrong-password rejection below; otherwise timing would reveal account existence.
            _passwordHasher.VerifyHashedPassword(new User(), DummyPasswordHash, request.Password);
            _logger.LogDebug("Login rejected for {Email}", email);
            return Result<AuthResponse>.Unauthorized("Invalid credentials.");
        }

        var verification = _passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);

        if (verification == PasswordVerificationResult.Failed)
        {
            _logger.LogDebug("Login rejected for {Email}", email);
            return Result<AuthResponse>.Unauthorized("Invalid credentials.");
        }

        return Result<AuthResponse>.Success(await IssueTokensAsync(user, cancellationToken));
    }

    public async Task<Result<AuthResponse>> RefreshAsync(
        RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = _tokenService.HashRefreshToken(request.RefreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, cancellationToken);

        if (stored is null)
        {
            return Result<AuthResponse>.Unauthorized("Invalid or expired refresh token.");
        }

        if (stored.RevokedAt is not null)
        {
            // A revoked token being presented again indicates replay, so every session for this
            // user is dropped rather than only rejecting this call.
            _logger.LogWarning("Refresh rejected: revoked token replayed for user {UserId}", stored.UserId);
            await _refreshTokens.RevokeAllForUserAsync(stored.UserId, cancellationToken);
            return Result<AuthResponse>.Unauthorized("Invalid or expired refresh token.");
        }

        if (stored.ExpiresAt <= DateTime.UtcNow)
        {
            // Ordinary end of life, not an attack. Reject this token only.
            return Result<AuthResponse>.Unauthorized("Invalid or expired refresh token.");
        }

        var user = await _users.GetByIdAsync(stored.UserId, cancellationToken);

        if (user is null)
        {
            return Result<AuthResponse>.Unauthorized("Invalid or expired refresh token.");
        }

        // Rotation: the presented token is revoked as soon as it is exchanged.
        await _refreshTokens.RevokeAsync(stored, cancellationToken);

        return Result<AuthResponse>.Success(await IssueTokensAsync(user, cancellationToken));
    }

    public async Task<Result> LogoutAsync(
        RefreshRequest request, CancellationToken cancellationToken = default)
    {
        var hash = _tokenService.HashRefreshToken(request.RefreshToken);
        var stored = await _refreshTokens.GetByHashAsync(hash, cancellationToken);

        if (stored is not null && stored.RevokedAt is null)
        {
            await _refreshTokens.RevokeAsync(stored, cancellationToken);
        }

        // Always succeeds. Reporting whether the token existed would leak its validity.
        return Result.Success();
    }

    private async Task<AuthResponse> IssueTokensAsync(User user, CancellationToken cancellationToken)
    {
        var (accessToken, expiresIn) = _tokenService.CreateAccessToken(user);
        var (refreshToken, refreshHash, refreshExpiresAt) = _tokenService.CreateRefreshToken();

        await _refreshTokens.AddAsync(
            new RefreshToken
            {
                UserId = user.Id,
                TokenHash = refreshHash,
                ExpiresAt = refreshExpiresAt
            },
            cancellationToken);

        return new AuthResponse
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            TokenType = "Bearer",
            ExpiresIn = expiresIn,
            User = user.ToDto()
        };
    }
}
