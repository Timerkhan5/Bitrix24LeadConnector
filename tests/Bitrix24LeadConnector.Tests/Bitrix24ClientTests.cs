using System.Net;
using Bitrix24LeadConnector.Api.Integrations.Bitrix24;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bitrix24LeadConnector.Tests;

public sealed class Bitrix24ClientTests
{
    private static readonly BitrixCreateRequest Request = new(1, new("Тест", "Тест", "", [new("PHONE", "WORK", "70000000000")]));

    private static Bitrix24Client Client(RecordingHandler handler, TimeSpan? timeout = null) => new(
        new HttpClient(handler) { BaseAddress = new Uri("https://example.invalid/rest/1/test-token/"), Timeout = timeout ?? TimeSpan.FromSeconds(10) },
        NullLogger<Bitrix24Client>.Instance);

    [Theory]
    [InlineData("{\"error\":\"INVALID_CREDENTIALS\",\"error_description\":\"private\"}", 401, BitrixFailure.Api)]
    [InlineData("{\"error\":\"ACCESS_DENIED\"}", 403, BitrixFailure.Api)]
    [InlineData("{\"error\":\"ERROR\"}", 200, BitrixFailure.Api)]
    [InlineData("{\"error\":\"\"}", 200, BitrixFailure.Api)]
    [InlineData("{\"error\":null,\"result\":{\"item\":{\"id\":123}}}", 200, BitrixFailure.Api)]
    [InlineData("{\"error\":\"QUERY_LIMIT_EXCEEDED\"}", 429, BitrixFailure.RateLimit)]
    [InlineData("<html>Unavailable</html>", 503, BitrixFailure.Unavailable)]
    [InlineData("{}", 504, BitrixFailure.Timeout)]
    [InlineData("not JSON", 200, BitrixFailure.InvalidResponse)]
    [InlineData("null", 200, BitrixFailure.InvalidResponse)]
    [InlineData("{}", 200, BitrixFailure.InvalidResponse)]
    [InlineData("{\"result\":{\"item\":{}}}", 200, BitrixFailure.InvalidResponse)]
    [InlineData("{\"result\":{\"item\":{\"id\":0}}}", 200, BitrixFailure.InvalidResponse)]
    [InlineData("{\"result\":{\"item\":{\"id\":-1}}}", 200, BitrixFailure.InvalidResponse)]
    public async Task CRM_failures_are_classified_without_retry(string body, int status, BitrixFailure expected)
    {
        using var handler = new RecordingHandler { Respond = _ => Task.FromResult(RecordingHandler.Json(body, (HttpStatusCode)status)) };
        var error = await Assert.ThrowsAsync<BitrixIntegrationException>(() => Client(handler).CreateAsync(Request, default));
        Assert.Equal(expected, error.Failure);
        Assert.DoesNotContain("private", error.ToString());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Network_failure_is_unavailable_and_does_not_expose_original_exception()
    {
        using var handler = new RecordingHandler { Respond = _ => throw new HttpRequestException("secret URL") };
        var error = await Assert.ThrowsAsync<BitrixIntegrationException>(() => Client(handler).CreateAsync(Request, default));
        Assert.Equal(BitrixFailure.Unavailable, error.Failure);
        Assert.DoesNotContain("secret URL", error.ToString());
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task HttpClient_timeout_is_translated_and_not_retried()
    {
        using var handler = new RecordingHandler
        {
            Respond = async token => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return RecordingHandler.Json(RecordingHandler.SuccessJson); }
        };
        var error = await Assert.ThrowsAsync<BitrixIntegrationException>(() => Client(handler, TimeSpan.FromMilliseconds(50)).CreateAsync(Request, default));
        Assert.Equal(BitrixFailure.Timeout, error.Failure);
        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task Caller_cancellation_is_propagated_instead_of_becoming_a_timeout()
    {
        using var source = new CancellationTokenSource();
        using var handler = new RecordingHandler
        {
            Respond = async token => { source.Cancel(); await Task.Delay(Timeout.InfiniteTimeSpan, token); return RecordingHandler.Json(RecordingHandler.SuccessJson); }
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Client(handler).CreateAsync(Request, source.Token));
        Assert.Equal(1, handler.Calls);
    }

    [Theory]
    [InlineData("{}")] [InlineData("{\"result\":{\"items\":null},\"total\":0}")]
    [InlineData("{\"result\":{\"items\":[null]},\"total\":1}")]
    [InlineData("{\"result\":{\"items\":[]},\"total\":0,\"next\":-1}")]
    public async Task Malformed_list_results_are_not_presented_as_success(string body)
    {
        using var handler = new RecordingHandler { Respond = _ => Task.FromResult(RecordingHandler.Json(body)) };
        var error = await Assert.ThrowsAsync<BitrixIntegrationException>(() => Client(handler).ListAsync(new(1, ["*"], 0), default));
        Assert.Equal(BitrixFailure.InvalidResponse, error.Failure);
    }
}
