using System.ComponentModel.DataAnnotations;

namespace EcommerceBackend.Application.DTOs.Admin;

public sealed record AdminLoginRequest
{
    [Required, StringLength(30, MinimumLength = 3)]
    public required string Username { get; init; }

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
    string Username,
    string Name,
    string? Email,
    string Role,
    long? TelegramId,
    bool IsActive,
    DateTime CreatedAt);

public sealed record CreateAdminRequest
{
    [Required, StringLength(30, MinimumLength = 3)]
    public required string Username { get; init; }

    [StringLength(100)]
    public string? Name { get; init; }

    [EmailAddress, StringLength(150)]
    public string? Email { get; init; }

    [Required, MinLength(6), StringLength(100)]
    public required string Password { get; init; }

    [StringLength(50)]
    public string Role { get; init; } = "Admin";

    public long? TelegramId { get; init; }
}

public sealed record UpdateAdminPasswordRequest
{
    [Required, StringLength(100)]
    public required string CurrentPassword { get; init; }

    [Required, MinLength(6), StringLength(100)]
    public required string NewPassword { get; init; }
}
