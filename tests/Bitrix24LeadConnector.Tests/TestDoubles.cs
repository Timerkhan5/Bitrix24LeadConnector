using System.Collections.Concurrent;
using System.Net;
using Bitrix24LeadConnector.Api.Integrations.Bitrix24;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bitrix24LeadConnector.Tests;

internal sealed class RecordingHandler : HttpMessageHandler
{
    public const string SuccessJson = """{"result":{"item":{"id":123}}}""";
    public int Calls { get; private set; }
    public string? LastBody { get; private set; }
    public Uri? LastUri { get; private set; }
    public Func<CancellationToken, Task<HttpResponseMessage>> Respond { get; set; } =
        _ => Task.FromResult(Json(SuccessJson));

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status) { Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json") };

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        LastUri = request.RequestUri;
        LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        return await Respond(cancellationToken);
    }
}

internal sealed class LogCollector : ILoggerProvider
{
    public ConcurrentQueue<string> Messages { get; } = new();
    public ILogger CreateLogger(string categoryName) => new Collector(Messages);
    public void Dispose() { }

    private sealed class Collector(ConcurrentQueue<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            messages.Enqueue(formatter(state, exception) + (exception is null ? "" : exception.ToString()));
    }
}

internal sealed class ApiFactory(string environment = "Development", bool validConfiguration = true) : WebApplicationFactory<Program>
{
    public RecordingHandler Handler { get; } = new();
    public LogCollector Logs { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(environment);
        builder.UseSetting("Bitrix24:WebhookBaseUrl", validConfiguration ? "https://example.invalid/rest/1/test-token/" : "");
        builder.ConfigureServices(services =>
        {
            services.AddHttpClient<IBitrix24Client, Bitrix24Client>()
                .ConfigurePrimaryHttpMessageHandler(() => Handler);
            services.AddLogging(logging => logging.AddProvider(Logs));
        });
    }
}
