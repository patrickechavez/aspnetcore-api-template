using System.ComponentModel.DataAnnotations;

namespace ApiTemplate.DTOs.Auth;

public class LoginRequest
{
    [Required, EmailAddress, MaxLength(255)]
    public required string Email { get; init; }

    [Required]
    public required string Password { get; init; }
}
