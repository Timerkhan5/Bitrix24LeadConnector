using Bitrix24LeadConnector.Api.Integrations.Bitrix24;
using Bitrix24LeadConnector.Api.Models;

namespace Bitrix24LeadConnector.Api.Services;

public sealed class LeadService(IBitrix24Client client, ILogger<LeadService> logger) : ILeadService
{
    public async Task<CreateLeadResponse> CreateAsync(CreateLeadRequest request, CancellationToken cancellationToken)
    {
        logger.LogInformation("Processing a lead submission");
        try
        {
            LeadValidation.Validate(request);
        }
        catch (LeadValidationException)
        {
            logger.LogWarning("Lead submission rejected by validation");
            throw;
        }

        var name = request.Name!.Trim();
        var fields = new BitrixLeadFields($"Заявка с сайта — {name}", name, request.Comment?.Trim() ?? "",
            [new BitrixMultiField("PHONE", "WORK", LeadValidation.NormalizePhone(request.Phone!))]);
        var id = await client.CreateAsync(new BitrixCreateRequest(1, fields), cancellationToken);
        logger.LogInformation("Lead created with ID {LeadId}", id);
        return new CreateLeadResponse(true, id, "Заявка успешно отправлена");
    }

    public async Task<LeadListResponse> ListAsync(int start, CancellationToken cancellationToken)
    {
        if (start < 0)
            throw new LeadValidationException(new Dictionary<string, string[]> { ["start"] = ["Позиция start должна быть неотрицательным целым числом"] });

        var result = await client.ListAsync(new BitrixListRequest(1, ["*"], start), cancellationToken);
        var items = result.Result!.Items!.Select(item => new LeadDto(item.Id, item.Title, item.Name,
            item.Fm?.FirstOrDefault(f => f?.TypeId == "PHONE")?.Value, item.Comments, item.CreatedTime)).ToArray();
        logger.LogInformation("Read {Count} leads at offset {Start}", items.Length, start);
        return new LeadListResponse(items, result.Total!.Value, result.Next);
    }
}
