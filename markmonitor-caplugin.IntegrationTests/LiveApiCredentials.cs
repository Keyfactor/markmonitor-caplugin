namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.IntegrationTests;

/// <summary>
/// Reads the MARKMONITOR_* environment variables that every live-API test in this project needs
/// (see .env / TestConsole/.env, sourced manually - there is no dotenv-loading package in this
/// repo). Every test in this project must skip (return early) rather than fail when these aren't
/// set, so the whole suite stays safe to run without live credentials present.
/// </summary>
internal static class LiveApiCredentials
{
    public static bool TryGet(out string? baseUrl, out string? apiToken, out string? username, out string? password)
    {
        baseUrl = Environment.GetEnvironmentVariable("MARKMONITOR_BASE_URL");
        apiToken = Environment.GetEnvironmentVariable("MARKMONITOR_API_TOKEN");
        username = Environment.GetEnvironmentVariable("MARKMONITOR_USERNAME");
        password = Environment.GetEnvironmentVariable("MARKMONITOR_PASSWORD");

        return !string.IsNullOrEmpty(baseUrl) && !string.IsNullOrEmpty(apiToken) &&
               !string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password);
    }
}
