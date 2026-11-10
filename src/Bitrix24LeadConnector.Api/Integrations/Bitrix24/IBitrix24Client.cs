namespace Bitrix24LeadConnector.Api.Integrations.Bitrix24;

public interface IBitrix24Client
{
    Task<long> CreateAsync(BitrixCreateRequest request, CancellationToken cancellationToken);
    Task<BitrixResponse<BitrixListResult>> ListAsync(BitrixListRequest request, CancellationToken cancellationToken);
}
