using ApiTemplate.DTOs.Users;

namespace ApiTemplate.DTOs.Auth;

public class AuthResponse
{
    public required string AccessToken { get; init; }

    public required string RefreshToken { get; init; }

    public required string TokenType { get; init; }

    public required int ExpiresIn { get; init; }

    public required UserDto User { get; init; }
}
