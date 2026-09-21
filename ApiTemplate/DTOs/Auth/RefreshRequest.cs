using System.ComponentModel.DataAnnotations;

namespace ApiTemplate.DTOs.Auth;

public class RefreshRequest
{
    [Required]
    public required string RefreshToken { get; init; }
}
