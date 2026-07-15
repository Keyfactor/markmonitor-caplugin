using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

public class MarkMonitorClientAuthenticateTests
{
    [Fact]
    public async Task AuthenticateAsync_WithFakeSuccessResponse_Succeeds()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);

        await client.AuthenticateAsync();

        var authRequest = Assert.Single(handler.Requests);
        Assert.Equal("POST", authRequest.Method.Method);
        Assert.Contains("/auth/v1/auth/authenticate", authRequest.RequestUri!.ToString());
    }

    [Fact]
    public async Task AuthenticateAsync_WithFailureResponse_ThrowsWithParsedErrorInsteadOfRawHttpException()
    {
        // Before the fix, EnsureSuccessStatusCode() was called before the response body was ever
        // read, so it always threw a bare HttpRequestException ("Response status code does not
        // indicate success: 401") - the code below it that builds a real error message from the
        // response body was unreachable dead code.
        var handler = new FakeHttpMessageHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/auth/v1/auth/authenticate"),
                FakeHttpMessageHandler.Json(HttpStatusCode.Unauthorized,
                    """{"errors":[{"code":"auth.invalidCredentials","message":"Invalid username or password."}]}"""));
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "pass", true, handler);

        var ex = await Assert.ThrowsAsync<Exception>(() => client.AuthenticateAsync());

        Assert.IsNotType<HttpRequestException>(ex);
        Assert.Contains("Invalid username or password", ex.Message);
    }
}
