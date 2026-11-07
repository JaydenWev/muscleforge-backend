using System.ComponentModel.DataAnnotations;

namespace MuscleForge.Api.Contracts;

public sealed class LoginRequest
{
    [Required(ErrorMessage = "Email is required.")]
    [EmailAddress(ErrorMessage = "Email must be valid.")]
    [StringLength(256, ErrorMessage = "Email must be at most 256 characters.")]
    public string? Email { get; init; }

    [Required(ErrorMessage = "Password is required.")]
    [StringLength(4096, ErrorMessage = "Password must be at most 4096 characters.")]
    public string? Password { get; init; }
}

public sealed class RefreshRequest
{
    [Required(ErrorMessage = "Refresh token is required.")]
    [StringLength(16384, ErrorMessage = "Refresh token must be at most 16384 characters.")]
    public string? RefreshToken { get; init; }
}

public sealed record CurrentUserResponse(Guid Id, string Email);
