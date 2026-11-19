using System.Security.Claims;
using EcommerceBackend.Application.Services;
using EcommerceBackend.Data;
using EcommerceBackend.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class AuditLogServiceTests
{
    [Fact]
    public async Task LogAsync_StagesEntryWithAuthenticatedUsernameAndJsonDetails()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext, CreateAccessor("manager"));

        await service.LogAsync(
            AuditLogActions.OrderStatusChanged,
            AuditLogEntityTypes.Order,
            Guid.NewGuid().ToString(),
            new { before = new { status = "New" }, after = new { status = "Confirmed" } });

        var entry = Assert.Single(dbContext.ChangeTracker.Entries<AdminAuditLog>());
        Assert.Equal(EntityState.Added, entry.State);
        Assert.Equal("manager", entry.Entity.AdminUsername);
        Assert.Equal(AuditLogActions.OrderStatusChanged, entry.Entity.Action);
        Assert.Contains("\"before\":{\"status\":\"New\"}", entry.Entity.Details);
        Assert.Contains("\"after\":{\"status\":\"Confirmed\"}", entry.Entity.Details);
    }

    [Fact]
    public async Task LogAsync_UsesSystemUsernameWhenHttpContextIsMissing()
    {
        await using var dbContext = CreateDbContext();
        var service = new AuditLogService(dbContext, new HttpContextAccessor());

        await service.LogAsync(
            AuditLogActions.ProductStockUpdated,
            AuditLogEntityTypes.Product,
            Guid.NewGuid().ToString(),
            new { stock = 10 });

        var entry = Assert.Single(dbContext.ChangeTracker.Entries<AdminAuditLog>());
        Assert.Equal("system", entry.Entity.AdminUsername);
    }

    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=test;Username=test;Password=test")
            .Options;

        return new AppDbContext(options);
    }

    private static IHttpContextAccessor CreateAccessor(string username)
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, username)], "Test");
        return new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(identity)
            }
        };
    }
}
