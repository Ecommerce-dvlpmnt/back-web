using EcommerceBackend.Application.DTOs.Orders;
using Xunit;

namespace EcommerceBackend.Tests;

public sealed class MonobankFiscalDiscountsCalculationTests
{
    [Fact]
    public void CalculateAmount_WithoutDiscounts_MatchesSumOfTotals()
    {
        // Приклад 1 з ТЗ: Латте 2шт (6000 коп) + Круасан 1шт (4500 коп) = 12000 + 4500 = 16500 (amount)
        var items = new List<MonobankFiscalItemDto>
        {
            new() { Name = "Кава Латте 300мл", Qty = 2, Sum = 6000, Total = 12000, Tax = [1] },
            new() { Name = "Круасан з шоколадом", Qty = 1, Sum = 4500, Total = 4500, Tax = [1] }
        };

        var amount = CalculateInvoiceAmount(items, null);
        Assert.Equal(16500, amount);
    }

    [Fact]
    public void CalculateAmount_WithCartPercentDiscount_CalculatesCorrectly()
    {
        // Приклад 2 з ТЗ: Total = 16500, знижка 10% = 1650, amount = 14850
        var items = new List<MonobankFiscalItemDto>
        {
            new() { Name = "Кава Латте 300мл", Qty = 2, Sum = 6000, Total = 12000, Tax = [1] },
            new() { Name = "Круасан з шоколадом", Qty = 1, Sum = 4500, Total = 4500, Tax = [1] }
        };

        var discounts = new List<MonobankDiscountDto>
        {
            new() { Type = "discount", Mode = "percent", Value = 10 }
        };

        var amount = CalculateInvoiceAmount(items, discounts);
        Assert.Equal(14850, amount);
    }

    [Fact]
    public void CalculateAmount_WithDeliveryExtraFee_CalculatesCorrectly()
    {
        // Приклад 3 з ТЗ: Total = 16500, extra (доставка) = 5000 (50 грн), amount = 21500
        var items = new List<MonobankFiscalItemDto>
        {
            new() { Name = "Кава Латте 300мл", Qty = 2, Sum = 6000, Total = 12000, Tax = [1] },
            new() { Name = "Круасан з шоколадом", Qty = 1, Sum = 4500, Total = 4500, Tax = [1] }
        };

        var extras = new List<MonobankDiscountDto>
        {
            new() { Type = "extra", Mode = "value", Value = 5000 }
        };

        var amount = CalculateInvoiceAmount(items, extras);
        Assert.Equal(21500, amount);
    }

    [Fact]
    public void CalculateAmount_WithItemLevelDiscount_CalculatesCorrectly()
    {
        // Знижка 5 грн (500 коп) на конкретний товар
        var items = new List<MonobankFiscalItemDto>
        {
            new()
            {
                Name = "Кава Латте 300мл",
                Qty = 1,
                Sum = 6000,
                Total = 6000,
                Tax = [1],
                Discounts = [new MonobankDiscountDto { Type = "discount", Mode = "value", Value = 500 }]
            }
        };

        var amount = CalculateInvoiceAmount(items, null);
        Assert.Equal(5500, amount);
    }

    private static long CalculateInvoiceAmount(
        IReadOnlyList<MonobankFiscalItemDto> basket,
        IReadOnlyList<MonobankDiscountDto>? cartDiscounts)
    {
        long subtotal = 0;
        foreach (var item in basket)
        {
            long lineTotal = item.Total > 0 ? item.Total : (long)Math.Round(item.Qty * item.Sum, MidpointRounding.AwayFromZero);
            long itemFinal = lineTotal;

            if (item.Discounts != null)
            {
                foreach (var d in item.Discounts)
                {
                    bool isDiscount = d.Type.Equals("discount", StringComparison.OrdinalIgnoreCase);
                    bool isPercent = d.Mode.Equals("percent", StringComparison.OrdinalIgnoreCase);

                    long diff = isPercent
                        ? (long)Math.Round(lineTotal * (d.Value / 100m), MidpointRounding.AwayFromZero)
                        : (long)d.Value;

                    itemFinal = isDiscount ? (itemFinal - diff) : (itemFinal + diff);
                }
            }

            subtotal += itemFinal;
        }

        long finalAmount = subtotal;
        if (cartDiscounts != null)
        {
            foreach (var d in cartDiscounts)
            {
                bool isDiscount = d.Type.Equals("discount", StringComparison.OrdinalIgnoreCase);
                bool isPercent = d.Mode.Equals("percent", StringComparison.OrdinalIgnoreCase);

                long diff = isPercent
                    ? (long)Math.Round(subtotal * (d.Value / 100m), MidpointRounding.AwayFromZero)
                    : (long)d.Value;

                finalAmount = isDiscount ? (finalAmount - diff) : (finalAmount + diff);
            }
        }

        return Math.Max(100, finalAmount);
    }
}
