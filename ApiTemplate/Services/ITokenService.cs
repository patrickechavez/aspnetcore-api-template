using ApiTemplate.Models;

namespace ApiTemplate.Services;

public interface ITokenService
{
    (string Token, int ExpiresInSeconds) CreateAccessToken(User user);

    (string Token, string TokenHash, DateTime ExpiresAt) CreateRefreshToken();

    string HashRefreshToken(string token);
}
