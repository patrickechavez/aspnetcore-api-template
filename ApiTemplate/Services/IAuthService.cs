using ApiTemplate.Common;
using ApiTemplate.DTOs.Auth;

namespace ApiTemplate.Services;

public interface IAuthService
{
    Task<Result<AuthResponse>> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken = default);

    Task<Result<AuthResponse>> LoginAsync(LoginRequest request, CancellationToken cancellationToken = default);

    Task<Result<AuthResponse>> RefreshAsync(RefreshRequest request, CancellationToken cancellationToken = default);

    Task<Result> LogoutAsync(RefreshRequest request, CancellationToken cancellationToken = default);
}
