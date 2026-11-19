namespace EcommerceBackend.Domain.Entities;

public class AdminAuditLog
{
    public Guid Id { get; set; }
    public string AdminUsername { get; set; } = null!;
    public string Action { get; set; } = null!;
    public string EntityType { get; set; } = null!;
    public string EntityId { get; set; } = null!;
    public string Details { get; set; } = "{}";
    public DateTime CreatedAt { get; set; }
}
