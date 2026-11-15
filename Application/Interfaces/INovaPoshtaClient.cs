using EcommerceBackend.Application.DTOs.NovaPoshta;

namespace EcommerceBackend.Application.Interfaces;

public interface INovaPoshtaClient
{
    Task<NovaPoshtaResult<NovaPoshtaCity>> GetCitiesAsync(
        string search,
        CancellationToken cancellationToken = default);

    Task<NovaPoshtaResult<NovaPoshtaWarehouse>> GetWarehousesAsync(
        string cityRef,
        CancellationToken cancellationToken = default);
}
