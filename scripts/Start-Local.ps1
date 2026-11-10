param([switch]$NoLaunchProfile)

$ErrorActionPreference = 'Stop'
$projectPath = Join-Path $PSScriptRoot '..\src\Bitrix24LeadConnector.Api\Bitrix24LeadConnector.Api.csproj'
$previousWebhook = $env:Bitrix24__WebhookBaseUrl
$previousEnvironment = $env:ASPNETCORE_ENVIRONMENT
$webhookPointer = [IntPtr]::Zero

try {
    if ([string]::IsNullOrWhiteSpace($env:Bitrix24__WebhookBaseUrl)) {
        $secureWebhook = Read-Host 'URL входящего вебхука тестового портала (ввод скрыт)' -AsSecureString
        $webhookPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureWebhook)
        $env:Bitrix24__WebhookBaseUrl = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($webhookPointer)
    }
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    if ($NoLaunchProfile) {
        & dotnet run --project $projectPath --no-launch-profile --urls 'http://localhost:5180'
    } else {
        & dotnet run --project $projectPath --launch-profile http
    }
    if ($LASTEXITCODE -ne 0) { throw 'Приложение завершилось с ошибкой. Проверьте сообщение конфигурации или сборки выше.' }
} finally {
    $env:Bitrix24__WebhookBaseUrl = $previousWebhook
    $env:ASPNETCORE_ENVIRONMENT = $previousEnvironment
    if ($webhookPointer -ne [IntPtr]::Zero) {
        [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($webhookPointer)
    }
    if ($null -ne $secureWebhook) { $secureWebhook.Dispose() }
}
