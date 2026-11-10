using System.ComponentModel.DataAnnotations;

namespace Bitrix24LeadConnector.Api.Models;

public sealed record CreateLeadRequest(
    [property: Required, StringLength(80, MinimumLength = 2)] string? Name,
    [property: Required] string? Phone,
    [property: StringLength(1000)] string? Comment = null);
public sealed record CreateLeadResponse(bool Success, long LeadId, string Message);
public sealed record LeadDto(long Id, string? Title, string? Name, string? Phone,
    string? Comments, DateTimeOffset? CreatedTime);
public sealed record LeadListResponse(IReadOnlyList<LeadDto> Items, long Total, int? NextStart);
public sealed record ApiError(bool Success, string Code, string Message);
public sealed record ValidationError(bool Success, string Code, IReadOnlyDictionary<string, string[]> Errors);
public sealed record HealthResponse(string Status);
