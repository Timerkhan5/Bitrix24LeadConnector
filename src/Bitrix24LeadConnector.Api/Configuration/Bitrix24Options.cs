using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Bitrix24LeadConnector.Api.Configuration;

public sealed class Bitrix24Options
{
    public const string SectionName = "Bitrix24";
    public string WebhookBaseUrl { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 10;
}

public sealed partial class Bitrix24OptionsValidator : IValidateOptions<Bitrix24Options>
{
    public ValidateOptionsResult Validate(string? name, Bitrix24Options options)
    {
        var errors = new List<string>();
        if (!Uri.TryCreate(options.WebhookBaseUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps
            || uri.IsLoopback
            || uri.HostNameType != UriHostNameType.Dns
            || !uri.IsDefaultPort
            || uri.UserInfo.Length != 0
            || uri.Query.Length != 0
            || uri.Fragment.Length != 0
            || !WebhookPath().IsMatch(uri.AbsolutePath))
        {
            errors.Add("Bitrix24:WebhookBaseUrl must be an HTTPS webhook base URL with path /rest/{userId}/{token}/. Set it using User Secrets or Bitrix24__WebhookBaseUrl. The configured value is not logged.");
        }

        if (options.TimeoutSeconds is < 1 or > 120)
            errors.Add("Bitrix24:TimeoutSeconds must be between 1 and 120.");

        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    [GeneratedRegex(@"^/rest/[1-9][0-9]*/[A-Za-z0-9_-]+/$", RegexOptions.CultureInvariant)]
    private static partial Regex WebhookPath();
}
