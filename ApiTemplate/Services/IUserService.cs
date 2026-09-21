using ApiTemplate.Common;
using ApiTemplate.DTOs.Users;

namespace ApiTemplate.Services;

public interface IUserService
{
    Task<Result<PagedResult<UserDto>>> GetPagedAsync(
        int page, int pageSize, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> GetCurrentAsync(CancellationToken cancellationToken = default);

    Task<Result<UserDto>> CreateAsync(CreateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result<UserDto>> UpdateAsync(
        Guid id, UpdateUserRequest request, CancellationToken cancellationToken = default);

    Task<Result> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
