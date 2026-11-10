using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Bitrix24LeadConnector.Api.Models;
using Microsoft.Extensions.Options;

namespace Bitrix24LeadConnector.Tests;

public sealed class ApiTests
{
    [Fact]
    public async Task Post_returns_201_and_expected_contract()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/leads", new CreateLeadRequest("Тестовый клиент", "+70000000000", "Тест"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<CreateLeadResponse>();
        Assert.True(result!.Success);
        Assert.Equal(123, result.LeadId);
        Assert.Equal("no-store", response.Headers.CacheControl!.ToString());
        Assert.Equal(1, factory.Handler.Calls);
    }

    [Theory]
    [InlineData("{\"name\":\" \",\"phone\":\"bad\"}", "name")]
    [InlineData("{\"name\":\"Тест\",\"phone\":null}", "phone")]
    [InlineData("{\"name\":42}", "body")]
    [InlineData("broken JSON", "body")]
    [InlineData("null", "body")]
    public async Task Invalid_requests_return_field_errors_without_calling_CRM(string json, string field)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/leads", new StringContent(json, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(result.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("VALIDATION_ERROR", result.RootElement.GetProperty("code").GetString());
        Assert.True(result.RootElement.GetProperty("errors").TryGetProperty(field, out _));
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Theory]
    [InlineData(401, 502, "BITRIX_API_ERROR")]
    [InlineData(403, 502, "BITRIX_API_ERROR")]
    [InlineData(503, 503, "BITRIX_UNAVAILABLE")]
    [InlineData(504, 504, "BITRIX_TIMEOUT")]
    [InlineData(429, 429, "BITRIX_RATE_LIMIT")]
    [InlineData(200, 502, "BITRIX_API_ERROR")]
    public async Task Integration_errors_use_safe_contract_and_logs(int crmStatus, int expectedStatus, string expectedCode)
    {
        const string sensitive = "https://example.invalid/rest/1/test-token/ Тестовый клиент +70000000000 секретный-комментарий";
        await using var factory = new ApiFactory();
        factory.Handler.Respond = _ => Task.FromResult(RecordingHandler.Json(JsonSerializer.Serialize(new { error = "ERROR", error_description = sensitive }), (HttpStatusCode)crmStatus));
        using var client = factory.CreateClient();
        using var response = await client.PostAsJsonAsync("/api/leads", new CreateLeadRequest("Тестовый клиент", "+70000000000", "секретный-комментарий"));
        Assert.Equal(expectedStatus, (int)response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ApiError>();
        Assert.False(result!.Success);
        Assert.Equal(expectedCode, result.Code);
        var output = await response.Content.ReadAsStringAsync() + string.Join("\n", factory.Logs.Messages);
        foreach (var value in new[] { "test-token", "+70000000000", "Тестовый клиент", "секретный-комментарий", "error_description" })
            Assert.DoesNotContain(value, output);
    }

    [Fact]
    public async Task Sixth_request_is_rate_limited_before_CRM()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            using var accepted = await client.PostAsJsonAsync("/api/leads", new CreateLeadRequest("Тест", "70000000000"));
            Assert.Equal(HttpStatusCode.Created, accepted.StatusCode);
        }
        using var rejected = await client.PostAsJsonAsync("/api/leads", new CreateLeadRequest("Тест", "70000000000"));
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.NotNull(rejected.Headers.RetryAfter);
        Assert.Equal("RATE_LIMIT_EXCEEDED", (await rejected.Content.ReadFromJsonAsync<ApiError>())!.Code);
        Assert.Equal(5, factory.Handler.Calls);
    }

    [Fact]
    public async Task Body_over_8_KB_is_rejected_before_CRM()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/leads", new StringContent(new string('a', 8193), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Fact]
    public async Task Chunked_body_over_8_KB_is_also_rejected()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var stream = new NonSeekableStream(Encoding.UTF8.GetBytes(new string('a', 8193)));
        using var body = new StreamContent(stream);
        body.Headers.ContentType = new("application/json");
        using var response = await client.PostAsync("/api/leads", body);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Fact]
    public async Task Non_JSON_content_type_is_rejected()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.PostAsync("/api/leads", new StringContent("{}"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Theory]
    [InlineData("-1")] [InlineData("abc")] [InlineData("2147483648")] [InlineData("")] [InlineData("1&start=2")]
    public async Task Invalid_start_returns_validation_error(string start)
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var response = await client.GetAsync($"/api/leads?start={start}");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Fact]
    public async Task Development_list_returns_simplified_page()
    {
        await using var factory = new ApiFactory();
        factory.Handler.Respond = _ => Task.FromResult(RecordingHandler.Json("""{"result":{"items":[]},"total":0}"""));
        using var client = factory.CreateClient();
        using var response = await client.GetAsync("/api/leads?start=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var page = await response.Content.ReadFromJsonAsync<LeadListResponse>();
        Assert.Empty(page!.Items);
        Assert.Null(page.NextStart);
        Assert.DoesNotContain("result", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, factory.Handler.Calls);
    }

    [Theory]
    [InlineData("Production")] [InlineData("Staging")]
    public async Task Outside_development_list_and_swagger_are_not_registered(string environment)
    {
        await using var factory = new ApiFactory(environment);
        using var client = factory.CreateClient();
        using var leads = await client.GetAsync("/api/leads");
        Assert.Equal(HttpStatusCode.MethodNotAllowed, leads.StatusCode); // POST exists at this path; GET does not.
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Fact]
    public async Task Health_does_not_contact_CRM_and_form_is_served()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal("healthy", (await client.GetFromJsonAsync<HealthResponse>("/health"))!.Status);
        using var form = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, form.StatusCode);
        Assert.Contains("lead-form", await form.Content.ReadAsStringAsync());
        Assert.Contains("frame-ancestors 'none'", form.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Fact]
    public async Task Development_OpenAPI_describes_contracts()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        using var json = JsonDocument.Parse(await client.GetStringAsync("/swagger/v1/swagger.json"));
        var leads = json.RootElement.GetProperty("paths").GetProperty("/api/leads");
        var post = leads.GetProperty("post");
        Assert.True(post.TryGetProperty("requestBody", out _));
        foreach (var status in new[] { "201", "400", "413", "429", "502", "503", "504" })
            Assert.True(post.GetProperty("responses").TryGetProperty(status, out _));
        Assert.True(leads.TryGetProperty("get", out _));
        var start = Assert.Single(leads.GetProperty("get").GetProperty("parameters").EnumerateArray());
        Assert.Equal("start", start.GetProperty("name").GetString());
        Assert.Equal("query", start.GetProperty("in").GetString());
        Assert.Equal(0, factory.Handler.Calls);
    }

    [Fact]
    public async Task Missing_webhook_fails_at_startup()
    {
        await using var factory = new ApiFactory(validConfiguration: false);
        var error = Assert.Throws<OptionsValidationException>(() => factory.CreateClient());
        Assert.Contains("WebhookBaseUrl", error.Message);
    }

    private sealed class NonSeekableStream(byte[] data) : Stream
    {
        private readonly MemoryStream inner = new(data);
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => inner.ReadAsync(buffer, cancellationToken);
        public override void Flush() => throw new NotSupportedException();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        protected override void Dispose(bool disposing) { if (disposing) inner.Dispose(); base.Dispose(disposing); }
    }
}
