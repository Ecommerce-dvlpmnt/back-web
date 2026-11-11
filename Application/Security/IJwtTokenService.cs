using EcommerceBackend.Domain.Entities;

namespace EcommerceBackend.Application.Security;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiresAt) GenerateToken(Admin admin);
}
