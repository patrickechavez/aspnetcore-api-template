using ApiTemplate.DTOs.Users;
using ApiTemplate.Models;

namespace ApiTemplate.Common.Mapping;

public static class UserMappings
{
    public static UserDto ToDto(this User user) => new()
    {
        Id = user.Id.ToString(),
        Email = user.Email,
        DisplayName = user.DisplayName,
        Role = user.Role.ToString()
    };
}
