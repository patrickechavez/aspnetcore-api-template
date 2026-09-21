using System.ComponentModel.DataAnnotations;

namespace ApiTemplate.DTOs.Users;

public class CreateUserRequest
{
    [Required, EmailAddress, MaxLength(255)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; init; }

    [MinLength(1), MaxLength(100)]
    public string? DisplayName { get; init; }

    [Required]
    [RegularExpression("^(Admin|User)$", ErrorMessage = "Role must be either 'Admin' or 'User'.")]
    public required string Role { get; init; }
}
