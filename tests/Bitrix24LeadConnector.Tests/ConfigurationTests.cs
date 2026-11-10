using Bitrix24LeadConnector.Api.Configuration;

namespace Bitrix24LeadConnector.Tests;

public sealed class ConfigurationTests
{
    [Theory]
    [InlineData("")]
    [InlineData("http://example.invalid/rest/1/test-token/")]
    [InlineData("https://localhost/rest/1/test-token/")]
    [InlineData("https://127.0.0.1/rest/1/test-token/")]
    [InlineData("https://example.invalid/rest/1/test-token")]
    [InlineData("https://example.invalid/rest/1/test-token/crm.item.add.json")]
    [InlineData("https://example.invalid/rest/1/test-token/?query=secret")]
    [InlineData("https://user:password@example.invalid/rest/1/test-token/")]
    [InlineData("https://example.invalid/rest/1/test-token/#fragment")]
    public void Invalid_webhook_configuration_is_rejected_without_echoing_the_value(string url)
    {
        var result = new Bitrix24OptionsValidator().Validate(null, new() { WebhookBaseUrl = url });
        Assert.True(result.Failed);
        Assert.DoesNotContain("test-token", result.FailureMessage);
        Assert.DoesNotContain("password", result.FailureMessage);
    }

    [Theory]
    [InlineData(0)] [InlineData(121)]
    public void Invalid_timeout_configuration_is_rejected(int timeout)
    {
        var result = new Bitrix24OptionsValidator().Validate(null, new() { WebhookBaseUrl = "https://example.invalid/rest/1/test-token/", TimeoutSeconds = timeout });
        Assert.True(result.Failed);
    }

    [Fact]
    public void Valid_configuration_is_accepted()
    {
        Assert.True(new Bitrix24OptionsValidator().Validate(null, new() { WebhookBaseUrl = "https://example.invalid/rest/1/test-token/" }).Succeeded);
    }
}
