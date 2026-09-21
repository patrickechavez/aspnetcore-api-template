using System.ComponentModel.DataAnnotations;

namespace ApiTemplate.DTOs.Auth;

public class RegisterRequest
{
    [Required, EmailAddress, MaxLength(255)]
    public required string Email { get; init; }

    [Required, MinLength(8), MaxLength(128)]
    public required string Password { get; init; }

    [MinLength(1), MaxLength(100)]
    public string? DisplayName { get; init; }
}
