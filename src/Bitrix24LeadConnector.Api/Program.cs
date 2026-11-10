using System.Threading.RateLimiting;
using Bitrix24LeadConnector.Api.Configuration;
using Bitrix24LeadConnector.Api.Endpoints;
using Bitrix24LeadConnector.Api.Integrations.Bitrix24;
using Bitrix24LeadConnector.Api.Models;
using Bitrix24LeadConnector.Api.Services;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = LeadEndpoints.MaxBodyBytes);
builder.Services.AddSingleton<IValidateOptions<Bitrix24Options>, Bitrix24OptionsValidator>();
builder.Services.AddOptions<Bitrix24Options>()
    .BindConfiguration(Bitrix24Options.SectionName).ValidateOnStart();
builder.Services.AddHttpClient<IBitrix24Client, Bitrix24Client>((services, client) =>
    {
        var options = services.GetRequiredService<IOptions<Bitrix24Options>>().Value;
        client.BaseAddress = new Uri(options.WebhookBaseUrl);
        client.Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds);
        client.MaxResponseContentBufferSize = 8 * 1024 * 1024;
    })
    .RemoveAllLoggers() // Default HttpClient loggers include the secret webhook path.
    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
builder.Services.AddScoped<ILeadService, LeadService>();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddRateLimiter(options =>
{
    options.OnRejected = async (context, cancellationToken) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        await context.HttpContext.Response.WriteAsJsonAsync(
            new ApiError(false, "RATE_LIMIT_EXCEEDED", "Слишком много заявок. Подождите минуту"), cancellationToken);
    };
    options.AddPolicy("lead-submissions", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0,
            AutoReplenishment = true
        }));
});

var app = builder.Build();
// Always return sanitized errors, including in Development; do not enable DeveloperExceptionPage.
app.UseExceptionHandler(new ExceptionHandlerOptions { SuppressDiagnosticsCallback = _ => true });
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'self'; script-src 'self'; style-src 'self'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'none'; form-action 'self'";
    // Swagger UI uses embedded inline initialization code and styles.
    if (app.Environment.IsDevelopment() && context.Request.Path.StartsWithSegments("/swagger"))
        context.Response.Headers.Remove("Content-Security-Policy");
    if (context.Request.Path.StartsWithSegments("/api"))
        context.Response.Headers.CacheControl = "no-store";
    await next(context);
});
app.UseDefaultFiles();
app.UseStaticFiles();
app.UseRateLimiter();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
app.MapLeadEndpoints();
app.MapGet("/health", () => new HealthResponse("healthy"))
    .WithName("Health").WithTags("Health").WithSummary("Проверить состояние приложения без обращения к CRM");
app.Run();

public partial class Program;
