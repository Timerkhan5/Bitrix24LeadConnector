using Bitrix24LeadConnector.Api.Models;

namespace Bitrix24LeadConnector.Api.Services;

public interface ILeadService
{
    Task<CreateLeadResponse> CreateAsync(CreateLeadRequest request, CancellationToken cancellationToken);
    Task<LeadListResponse> ListAsync(int start, CancellationToken cancellationToken);
}
