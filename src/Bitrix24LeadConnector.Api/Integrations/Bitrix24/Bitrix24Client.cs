using System.Net;
using System.Text.Json;

namespace Bitrix24LeadConnector.Api.Integrations.Bitrix24;

public sealed class Bitrix24Client(HttpClient httpClient, ILogger<Bitrix24Client> logger) : IBitrix24Client
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<long> CreateAsync(BitrixCreateRequest request, CancellationToken cancellationToken)
    {
        var response = await SendAsync<BitrixCreateRequest, BitrixCreateResult>("crm.item.add.json", request, cancellationToken);
        if (response.Result?.Item is not { Id: > 0 } item)
            throw InvalidResponse("crm.item.add.json");
        return item.Id;
    }

    public async Task<BitrixResponse<BitrixListResult>> ListAsync(BitrixListRequest request, CancellationToken cancellationToken)
    {
        var response = await SendAsync<BitrixListRequest, BitrixListResult>("crm.item.list.json", request, cancellationToken);
        if (response.Result?.Items is not { } items || items.Any(i => i is null || i.Id <= 0)
            || response.Total is null or < 0 || response.Next is < 0)
            throw InvalidResponse("crm.item.list.json");
        return response;
    }

    private async Task<BitrixResponse<TResponse>> SendAsync<TRequest, TResponse>(string method, TRequest payload, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, method)
            {
                Content = JsonContent.Create(payload, options: JsonOptions)
            };
            // No retries: an interrupted creation request may already have created a lead.
            using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseContentRead, cancellationToken);
            var body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

            // Parse every response, including HTTP errors, because Bitrix24 has business-level errors.
            BitrixResponse<TResponse>? envelope = null;
            try
            {
                envelope = JsonSerializer.Deserialize<BitrixResponse<TResponse>>(body, JsonOptions);
            }
            catch (JsonException)
            {
                logger.LogError("Invalid JSON in CRM response for {Method}, HTTP {Status}", method, (int)response.StatusCode);
                if (response.IsSuccessStatusCode)
                    throw new BitrixIntegrationException(BitrixFailure.InvalidResponse);
            }

            if (!response.IsSuccessStatusCode || envelope?.Error.ValueKind is not (null or JsonValueKind.Undefined))
            {
                var failure = response.StatusCode switch
                {
                    HttpStatusCode.TooManyRequests => BitrixFailure.RateLimit,
                    HttpStatusCode.RequestTimeout or HttpStatusCode.GatewayTimeout => BitrixFailure.Timeout,
                    HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable => BitrixFailure.Unavailable,
                    _ when (int)response.StatusCode >= 500 => BitrixFailure.Unavailable,
                    _ => BitrixFailure.Api
                };
                // Log only an allowlisted classification, never error_description or exception details.
                if (failure == BitrixFailure.RateLimit)
                    logger.LogWarning("CRM rate limit for {Method}, HTTP {Status}", method, (int)response.StatusCode);
                else
                    logger.LogError("CRM API error for {Method}, HTTP {Status}", method, (int)response.StatusCode);
                throw new BitrixIntegrationException(failure);
            }

            return envelope ?? throw InvalidResponse(method);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            logger.LogError("CRM request timed out for {Method}", method);
            throw new BitrixIntegrationException(BitrixFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            logger.LogError("CRM transport unavailable for {Method}", method);
            throw new BitrixIntegrationException(BitrixFailure.Unavailable);
        }
    }

    private BitrixIntegrationException InvalidResponse(string method)
    {
        logger.LogError("CRM response is missing required result data for {Method}", method);
        return new BitrixIntegrationException(BitrixFailure.InvalidResponse);
    }
}
