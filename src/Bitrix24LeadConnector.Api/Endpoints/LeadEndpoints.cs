using System.Globalization;
using System.Text.Json;
using Bitrix24LeadConnector.Api.Models;
using Bitrix24LeadConnector.Api.Services;
using Microsoft.AspNetCore.Mvc;

namespace Bitrix24LeadConnector.Api.Endpoints;

public static class LeadEndpoints
{
    public const int MaxBodyBytes = 8 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static void MapLeadEndpoints(this WebApplication app)
    {
        app.MapPost("/api/leads", CreateAsync)
            .WithName("CreateLead").WithTags("Leads")
            .WithSummary("Создать лид в Битрикс24")
            .WithDescription("Имя: 2–80 символов после Trim. Телефон: 10–15 цифр, начальный +, пробелы, дефисы и скобки. Комментарий: до 1000 символов. До 5 запросов в минуту на IP; JSON до 8 КБ. Автоматических повторов нет.")
            .Accepts<CreateLeadRequest>("application/json")
            .Produces<CreateLeadResponse>(201)
            .Produces<ValidationError>(400)
            .Produces<ApiError>(413).Produces<ApiError>(415).Produces<ApiError>(429)
            .Produces<ApiError>(502).Produces<ApiError>(503).Produces<ApiError>(504)
            .RequireRateLimiting("lead-submissions");

        if (app.Environment.IsDevelopment())
        {
            app.MapGet("/api/leads", ListAsync)
                .WithName("ListLeads").WithTags("Leads")
                .WithSummary("Получить страницу лидов (только Development)")
                .WithDescription("Параметр start — неотрицательное целое смещение. Для следующей страницы используйте nextStart из ответа. Данные читаются из CRM.")
                .Produces<LeadListResponse>().Produces<ValidationError>(400)
                .Produces<ApiError>(429).Produces<ApiError>(502).Produces<ApiError>(503).Produces<ApiError>(504);
        }
    }

    private static async Task<IResult> CreateAsync(HttpRequest request, ILeadService service, ILoggerFactory loggerFactory, CancellationToken cancellationToken)
    {
        if (request.ContentLength > MaxBodyBytes)
            return TooLarge();
        if (!request.HasJsonContentType())
            return Results.Json(new ApiError(false, "UNSUPPORTED_MEDIA_TYPE", "Используйте Content-Type: application/json"), statusCode: 415);

        CreateLeadRequest? model;
        try
        {
            // A bounded read also enforces the limit for chunked requests and TestServer.
            var buffer = new byte[MaxBodyBytes + 1];
            var count = 0;
            while (count < buffer.Length)
            {
                var read = await request.Body.ReadAsync(buffer.AsMemory(count), cancellationToken);
                if (read == 0) break;
                count += read;
            }
            if (count > MaxBodyBytes) return TooLarge();
            model = JsonSerializer.Deserialize<CreateLeadRequest>(buffer.AsSpan(0, count), JsonOptions);
        }
        catch (JsonException)
        {
            loggerFactory.CreateLogger("LeadEndpoints").LogWarning("Lead submission rejected: invalid JSON");
            return InvalidBody();
        }
        catch (BadHttpRequestException exception) when (exception.StatusCode == 413)
        {
            return TooLarge();
        }

        if (model is null) return InvalidBody();
        return Results.Json(await service.CreateAsync(model, cancellationToken), statusCode: 201);
    }

    private static async Task<IResult> ListAsync(HttpRequest request, [FromQuery(Name = "start")] string? start, ILeadService service, CancellationToken cancellationToken)
    {
        var values = request.Query["start"];
        var offset = 0;
        if (values.Count > 0 && (values.Count != 1 || !int.TryParse(start, NumberStyles.None, CultureInfo.InvariantCulture, out offset)))
            throw new LeadValidationException(new Dictionary<string, string[]> { ["start"] = ["Позиция start должна быть неотрицательным целым числом"] });
        return Results.Ok(await service.ListAsync(offset, cancellationToken));
    }

    private static IResult TooLarge() => Results.Json(new ApiError(false, "REQUEST_TOO_LARGE", "Размер запроса не должен превышать 8 КБ"), statusCode: 413);
    private static IResult InvalidBody() => Results.Json(new ValidationError(false, "VALIDATION_ERROR",
        new Dictionary<string, string[]> { ["body"] = ["Передайте корректный JSON-объект заявки"] }), statusCode: 400);
}
