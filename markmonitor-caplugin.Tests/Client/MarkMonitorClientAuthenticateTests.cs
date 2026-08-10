using System.Net;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.Client;

[Collection(LogHandlerFactoryCollection.Name)]
public class MarkMonitorClientAuthenticateTests
{
    [Fact]
    public async Task AuthenticateAsync_WithFakeSuccessResponse_Succeeds()
    {
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = handler.BuildClient();

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
        var client = handler.BuildClient();

        var ex = await Assert.ThrowsAsync<Exception>(() => client.AuthenticateAsync());

        Assert.IsNotType<HttpRequestException>(ex);
        Assert.Contains("Invalid username or password", ex.Message);
    }

    [Fact]
    public async Task AuthenticateAsync_CalledTwice_DoesNotDuplicateTheApiKeyHeader()
    {
        // DefaultRequestHeaders.Add() does not replace an existing value for the same header name -
        // calling AuthenticateAsync a second time on the same client (now a real path, since
        // EnsureAuthenticatedAsync re-authenticates on token expiry) used to leave two X-API-KEY
        // values on every subsequent request.
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var client = handler.BuildClient();

        await client.AuthenticateAsync();
        await client.AuthenticateAsync();

        var lastRequest = handler.Requests.Last();
        var apiKeyValues = lastRequest.Headers.GetValues("X-API-KEY").ToList();
        Assert.Single(apiKeyValues);
        Assert.Equal("key", apiKeyValues[0]);
    }

    [Fact]
    public async Task AuthenticateAsync_WithASuccessStatusButAnEmptyBody_LogsAuthenticationFailedForTheUsername()
    {
        // Regression test: a 2xx response whose body is empty/malformed deserializes to null (or
        // throws on malformed JSON) without ever reaching the else-branch's own "Authentication
        // failed" log - neither of AuthenticateAsync's other two failure-logging sites covered this
        // case, so it used to surface only as a generic, identity-less error from whichever caller's
        // catch block received the exception instead of this method's own identity-tagged record.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        var handler = new FakeHttpMessageHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/auth/v1/auth/authenticate"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "null"));
        var client = handler.BuildClient();

        await Assert.ThrowsAnyAsync<Exception>(() => client.AuthenticateAsync());

        Assert.Contains(capturingFactory.Messages,
            m => m.Contains("Authentication failed for", StringComparison.Ordinal) &&
                 m.Contains("user", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticateAsync_WithASuccessStatusButNoTokenField_LogsAuthenticationFailedForTheUsername()
    {
        // Regression test: a well-formed 2xx JSON body simply missing (or empty on) the "token" field
        // deserializes successfully to a non-null TokenResponse with BearerToken null - the null-body
        // guard alone didn't catch this, so it used to fall through, set an empty/absent bearer token,
        // and log "Authentication successful" instead of failing loudly.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        var handler = new FakeHttpMessageHandler()
            .When(req => FakeHttpMessageHandler.Is(req, "POST", "/auth/v1/auth/authenticate"),
                FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{}"));
        var client = handler.BuildClient();

        await Assert.ThrowsAnyAsync<Exception>(() => client.AuthenticateAsync());

        Assert.Contains(capturingFactory.Messages,
            m => m.Contains("Authentication failed for", StringComparison.Ordinal) &&
                 m.Contains("user", StringComparison.Ordinal));
        Assert.DoesNotContain(capturingFactory.Messages,
            m => m.Contains("Authentication successful", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AuthenticateAsync_WithMissingConfig_LogsAuthenticationFailedForTheUsername()
    {
        // Regression test: ValidateConfiguration()'s throw used to sit outside this method's try/catch
        // entirely, so a missing ApiKey/Username/Password (e.g. Enabled=true with a blank ApiPassword,
        // a state the plugin's own config comments explicitly anticipate) produced zero identity-
        // tagged authentication-failure record from this method - only whichever generic, identity-
        // less message an enclosing caller's own catch happened to log.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        var handler = new FakeHttpMessageHandler();
        var client = new MarkMonitorClient("https://api.markmonitor.test", "key", "user", "", true, handler);

        await Assert.ThrowsAnyAsync<Exception>(() => client.AuthenticateAsync());

        Assert.Contains(capturingFactory.Messages,
            m => m.Contains("Authentication failed for", StringComparison.Ordinal) &&
                 m.Contains("user", StringComparison.Ordinal) &&
                 m.Contains("Password is required", StringComparison.Ordinal));
        Assert.Empty(handler.Requests);
    }
}
