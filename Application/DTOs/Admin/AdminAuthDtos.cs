using System.ComponentModel.DataAnnotations;

namespace EcommerceBackend.Application.DTOs.Admin;

public sealed record AdminLoginRequest
{
    [Required, EmailAddress, StringLength(150)]
    public required string Email { get; init; }

    [Required, MinLength(6), StringLength(100)]
    public required string Password { get; init; }
}

public sealed record AdminLoginResponse(
    string Token,
    string TokenType,
    DateTime ExpiresAt,
    AdminResponse Admin);

public sealed record AdminResponse(
    Guid Id,
    string Name,
    string Email,
    string Role,
    long? TelegramId,
    bool IsActive,
    DateTime CreatedAt);

public sealed record CreateAdminRequest
{
    [Required, StringLength(100)]
    public required string Name { get; init; }

    [Required, EmailAddress, StringLength(150)]
    public required string Email { get; init; }

    [Required, MinLength(6), StringLength(100)]
    public required string Password { get; init; }

    [StringLength(50)]
    public string Role { get; init; } = "Manager";

    public long? TelegramId { get; init; }
}

public sealed record UpdateAdminPasswordRequest
{
    [Required, StringLength(100)]
    public required string CurrentPassword { get; init; }

    [Required, MinLength(6), StringLength(100)]
    public required string NewPassword { get; init; }
}
