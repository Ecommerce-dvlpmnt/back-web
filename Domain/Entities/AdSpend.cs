namespace EcommerceBackend.Domain.Entities;

public class AdSpend
{
    public Guid Id { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountUsd { get; set; }
    public decimal ExchangeRate { get; set; }
    public string Source { get; set; } = null!;
    public DateTime Date { get; set; }
    public string? Note { get; set; }
    public Guid? RecordedByAdminId { get; set; }
    public DateTime CreatedAt { get; set; }

    public Admin? RecordedByAdmin { get; set; }
}
