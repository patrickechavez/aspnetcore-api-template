using System.ComponentModel.DataAnnotations;

namespace ApiTemplate.DTOs.Users;

public class UpdateUserRequest
{
    [MinLength(1), MaxLength(100)]
    public string? DisplayName { get; init; }

    [RegularExpression("^(Admin|User)$", ErrorMessage = "Role must be either 'Admin' or 'User'.")]
    public string? Role { get; init; }
}
