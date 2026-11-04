namespace EcommerceBackend.Domain.Entities;

public class Campaign
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Slug { get; set; } = null!;
    public DateTime StartsAt { get; set; }
    public DateTime EndsAt { get; set; }
    public string? BannerUrl { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<CampaignProduct> Products { get; set; } = new List<CampaignProduct>();
    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
