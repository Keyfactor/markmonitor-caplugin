using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientErrorMessageRedactionTests
{
    [Fact]
    public async Task UnrecognizedErrorShape_IsTruncatedRatherThanDumpedVerbatim()
    {
        // Order/contact payloads can carry customer PII (name, email). An unrecognized MarkMonitor
        // error shape used to get dumped whole into the exception message / error-level logs.
        var contactPii = "Jane Doe, jane.doe@example.com, " + new string('x', 300);
        var unrecognizedBody = "{\"someUnexpectedField\": \"" + contactPii + "\"}";
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.InternalServerError, unrecognizedBody));
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() => client.ListOrganizationsAsync());

        Assert.True(ex.Message.Length < unrecognizedBody.Length);
        Assert.Contains("(truncated)", ex.Message);
        Assert.DoesNotContain(new string('x', 300), ex.Message);
    }
}
