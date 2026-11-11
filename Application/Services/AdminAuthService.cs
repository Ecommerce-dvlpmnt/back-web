using EcommerceBackend.Application.DTOs.Admin;
using EcommerceBackend.Application.Interfaces;
using EcommerceBackend.Application.Security;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace EcommerceBackend.Application.Services;

public sealed class AdminAuthService(
    AppDbContext dbContext,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService) : IAdminAuthService
{
    public async Task<AdminLoginResponse> LoginAsync(
        AdminLoginRequest request,
        CancellationToken ct = default)
    {
        var username = request.Username.Trim().ToLowerInvariant();
        var admin = await dbContext.Admins
            .FirstOrDefaultAsync(a => a.Username.ToLower() == username, ct);

        if (admin is null || !passwordHasher.VerifyPassword(request.Password, admin.PasswordHash))
        {
            throw new UnauthorizedAccessException("Неверный логин или пароль.");
        }

        if (!admin.IsActive)
        {
            throw new UnauthorizedAccessException("Учётная запись администратора деактивирована.");
        }

        var (token, expiresAt) = jwtTokenService.GenerateToken(admin);

        return new AdminLoginResponse(
            Token: token,
            TokenType: "Bearer",
            ExpiresAt: expiresAt,
            Admin: ToDto(admin));
    }

    public async Task<AdminResponse?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var admin = await dbContext.Admins.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        return admin is null ? null : ToDto(admin);
    }

    public async Task<AdminResponse> CreateAsync(
        CreateAdminRequest request,
        CancellationToken ct = default)
    {
        var username = request.Username.Trim();
        var exists = await dbContext.Admins.AnyAsync(a => a.Username.ToLower() == username.ToLower(), ct);
        if (exists)
        {
            throw new InvalidOperationException($"Администратор с никнеймом '{request.Username}' уже существует.");
        }

        var now = DateTime.UtcNow;
        var admin = new Admin
        {
            Id = Guid.NewGuid(),
            Username = username,
            Name = string.IsNullOrWhiteSpace(request.Name) ? username : request.Name.Trim(),
            Email = string.IsNullOrWhiteSpace(request.Email) ? null : request.Email.Trim().ToLowerInvariant(),
            PasswordHash = passwordHasher.HashPassword(request.Password),
            Role = string.IsNullOrWhiteSpace(request.Role) ? "Admin" : request.Role.Trim(),
            TelegramId = request.TelegramId,
            IsActive = true,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Admins.Add(admin);
        await dbContext.SaveChangesAsync(ct);

        return ToDto(admin);
    }

    public async Task<IReadOnlyList<AdminResponse>> GetAllAsync(CancellationToken ct = default)
    {
        var admins = await dbContext.Admins
            .AsNoTracking()
            .OrderBy(a => a.Username)
            .ToListAsync(ct);

        return admins.Select(ToDto).ToList();
    }

    public async Task ChangePasswordAsync(
        Guid adminId,
        UpdateAdminPasswordRequest request,
        CancellationToken ct = default)
    {
        var admin = await dbContext.Admins.FirstOrDefaultAsync(a => a.Id == adminId, ct);
        if (admin is null)
        {
            throw new KeyNotFoundException("Администратор не найден.");
        }

        if (!passwordHasher.VerifyPassword(request.CurrentPassword, admin.PasswordHash))
        {
            throw new UnauthorizedAccessException("Текущий пароль указан неверно.");
        }

        admin.PasswordHash = passwordHasher.HashPassword(request.NewPassword);
        admin.UpdatedAt = DateTime.UtcNow;

        await dbContext.SaveChangesAsync(ct);
    }

    private static AdminResponse ToDto(Admin admin) => new(
        Id: admin.Id,
        Username: admin.Username,
        Name: admin.Name,
        Email: admin.Email,
        Role: admin.Role,
        TelegramId: admin.TelegramId,
        IsActive: admin.IsActive,
        CreatedAt: admin.CreatedAt);
}
