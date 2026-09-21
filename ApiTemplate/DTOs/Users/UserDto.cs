namespace ApiTemplate.DTOs.Users;

public class UserDto
{
    public required string Id { get; init; }

    public required string Email { get; init; }

    public string? DisplayName { get; init; }

    public required string Role { get; init; }
}
