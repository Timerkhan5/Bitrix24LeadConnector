using System.Text.RegularExpressions;
using Bitrix24LeadConnector.Api.Models;

namespace Bitrix24LeadConnector.Api.Services;

public sealed class LeadValidationException(IReadOnlyDictionary<string, string[]> errors)
    : Exception("Lead validation failed.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
}

public static partial class LeadValidation
{
    public static void Validate(CreateLeadRequest request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request.Name?.Trim().Length is not (>= 2 and <= 80))
            errors["name"] = ["Имя должно содержать от 2 до 80 символов"];

        if (!IsValidPhone(request.Phone))
            errors["phone"] = ["Телефон должен содержать от 10 до 15 цифр; допустимы начальный +, пробелы, дефисы и скобки"];

        if (request.Comment?.Length > 1000)
            errors["comment"] = ["Комментарий должен содержать не более 1000 символов"];

        if (errors.Count != 0)
            throw new LeadValidationException(errors);
    }

    private static bool IsValidPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone) || !PhoneCharacters().IsMatch(phone.Trim()))
            return false;
        return phone.Count(char.IsAsciiDigit) is >= 10 and <= 15;
    }

    public static string NormalizePhone(string phone) =>
        new(phone.Trim().Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());

    [GeneratedRegex(@"^\+?[0-9 ()-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex PhoneCharacters();
}
