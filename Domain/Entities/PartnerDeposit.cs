namespace EcommerceBackend.Domain.Entities;

public class PartnerDeposit
{
    public Guid Id { get; set; }
    public Guid AdminId { get; set; }
    public decimal Amount { get; set; }
    public decimal AmountUsd { get; set; }
    public decimal ExchangeRate { get; set; }
    public DateTime Date { get; set; }
    public string? Note { get; set; }
    public DateTime CreatedAt { get; set; }

    public Admin Admin { get; set; } = null!;
}
