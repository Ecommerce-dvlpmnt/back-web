using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using EcommerceBackend.Domain.Entities;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace EcommerceBackend.Application.Security;

public sealed class JwtTokenService(IConfiguration configuration) : IJwtTokenService
{
    public (string Token, DateTime ExpiresAt) GenerateToken(Admin admin)
    {
        var secret = configuration["Jwt:Secret"];
        if (string.IsNullOrWhiteSpace(secret) || secret.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:Secret is not configured or is too short. It must be at least 32 characters long.");
        }

        var issuer = configuration["Jwt:Issuer"] ?? "EcommerceBackend";
        var audience = configuration["Jwt:Audience"] ?? "EcommerceAdmin";
        var expiryHours = configuration.GetValue<int>("Jwt:ExpiryHours", 24);

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var expiresAt = DateTime.UtcNow.AddHours(expiryHours);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, admin.Id.ToString()),
            new(ClaimTypes.Name, admin.Username),
            new("nickname", admin.Username),
            new(ClaimTypes.Role, admin.Role),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        if (!string.IsNullOrWhiteSpace(admin.Email))
        {
            claims.Add(new Claim(ClaimTypes.Email, admin.Email));
        }

        if (admin.TelegramId.HasValue)
        {
            claims.Add(new Claim("telegram_id", admin.TelegramId.Value.ToString()));
        }

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = expiresAt,
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = credentials
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return (tokenHandler.WriteToken(token), expiresAt);
    }
}
