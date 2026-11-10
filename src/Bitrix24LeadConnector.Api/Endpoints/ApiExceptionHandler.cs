using Bitrix24LeadConnector.Api.Integrations.Bitrix24;
using Bitrix24LeadConnector.Api.Models;
using Bitrix24LeadConnector.Api.Services;
using Microsoft.AspNetCore.Diagnostics;

namespace Bitrix24LeadConnector.Api.Endpoints;

public sealed class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
        {
            context.Response.StatusCode = 499;
            return true;
        }

        if (exception is LeadValidationException validation)
        {
            context.Response.StatusCode = 400;
            await context.Response.WriteAsJsonAsync(new ValidationError(false, "VALIDATION_ERROR", validation.Errors), cancellationToken);
            return true;
        }

        var (status, code, message) = exception switch
        {
            BitrixIntegrationException { Failure: BitrixFailure.Timeout } => (504, "BITRIX_TIMEOUT", "Время ожидания CRM истекло. Заявка могла быть создана; уточните результат перед повторной отправкой"),
            BitrixIntegrationException { Failure: BitrixFailure.Unavailable } => (503, "BITRIX_UNAVAILABLE", "CRM временно недоступна. Если запрос уже отправлялся, уточните результат перед повторной отправкой"),
            BitrixIntegrationException { Failure: BitrixFailure.RateLimit } => (429, "BITRIX_RATE_LIMIT", "CRM ограничила число запросов. Попробуйте позже"),
            BitrixIntegrationException => (502, "BITRIX_API_ERROR", "Не удалось передать заявку в CRM"),
            _ => (500, "INTERNAL_ERROR", "Не удалось обработать запрос")
        };

        if (status == 500)
            logger.LogError("Unhandled request failure of type {ExceptionType}", exception.GetType().Name);
        context.Response.StatusCode = status;
        await context.Response.WriteAsJsonAsync(new ApiError(false, code, message), cancellationToken);
        return true;
    }
}
