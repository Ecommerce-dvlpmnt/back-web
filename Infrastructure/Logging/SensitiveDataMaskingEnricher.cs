using EcommerceBackend.Application.Security;
using Serilog.Core;
using Serilog.Events;

namespace EcommerceBackend.Infrastructure.Logging;

/// <summary>
/// Serilog Enricher, который автоматически перехватывает и маскирует чувствительные данные
/// (пароли, JWT, API-ключи, телефоны, email) во всех входящих свойствах логов перед записью в консоль и файлы.
/// </summary>
public sealed class SensitiveDataMaskingEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        // Итерируемся по копии списка свойств, чтобы безопасно модифицировать словарь
        foreach (var (key, value) in logEvent.Properties.ToList())
        {
            if (value is ScalarValue scalar && scalar.Value is string stringValue)
            {
                if (DataMasking.IsSensitiveKey(key))
                {
                    var masked = DataMasking.MaskSecret(stringValue);
                    logEvent.AddOrUpdateProperty(new LogEventProperty(key, new ScalarValue(masked)));
                }
                else if (key.Contains("phone", StringComparison.OrdinalIgnoreCase))
                {
                    var masked = DataMasking.MaskPhone(stringValue);
                    logEvent.AddOrUpdateProperty(new LogEventProperty(key, new ScalarValue(masked)));
                }
                else if (key.Contains("email", StringComparison.OrdinalIgnoreCase))
                {
                    var masked = DataMasking.MaskEmail(stringValue);
                    logEvent.AddOrUpdateProperty(new LogEventProperty(key, new ScalarValue(masked)));
                }
            }
        }
    }
}
