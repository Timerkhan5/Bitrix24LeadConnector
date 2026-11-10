using System.Text.Json;
using System.Text.Json.Serialization;

namespace Bitrix24LeadConnector.Api.Integrations.Bitrix24;

public sealed record BitrixCreateRequest(int EntityTypeId, BitrixLeadFields Fields);
public sealed record BitrixLeadFields(string Title, string Name, string Comments, BitrixMultiField[] Fm);
public sealed record BitrixMultiField(string? TypeId, string? ValueType, string? Value);
public sealed record BitrixListRequest(int EntityTypeId, string[] Select, int Start);
public sealed record BitrixCreateResult(BitrixLead? Item);
public sealed record BitrixListResult(BitrixLead[]? Items);
public sealed record BitrixResponse<T>(T? Result, JsonElement Error, long? Total, int? Next);
public sealed record BitrixLead(
    [property: JsonNumberHandling(JsonNumberHandling.AllowReadingFromString)] long Id,
    string? Title, string? Name, string? Comments, DateTimeOffset? CreatedTime, BitrixMultiField[]? Fm);

public enum BitrixFailure { Api, Unavailable, Timeout, RateLimit, InvalidResponse }

// Exception messages are deliberately static; upstream messages and URLs may contain secrets or PII.
public sealed class BitrixIntegrationException(BitrixFailure failure) : Exception($"Bitrix24 integration failed: {failure}.")
{
    public BitrixFailure Failure { get; } = failure;
}
