using EcommerceBackend.Application.DTOs.Admin;

namespace EcommerceBackend.Application.Interfaces;

public interface IAdminAuthService
{
    Task<AdminLoginResponse> LoginAsync(AdminLoginRequest request, CancellationToken ct = default);
    Task<AdminResponse?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task<AdminResponse> CreateAsync(CreateAdminRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<AdminResponse>> GetAllAsync(CancellationToken ct = default);
    Task ChangePasswordAsync(Guid adminId, UpdateAdminPasswordRequest request, CancellationToken ct = default);
    Task DeleteAsync(Guid id, Guid currentAdminId, CancellationToken ct = default);
}
