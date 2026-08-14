// Copyright 2026 Keyfactor
// 
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
// 
//     http://www.apache.org/licenses/LICENSE-2.0
// 
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

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
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() => client.ListOrganizationsAsync());

        Assert.True(ex.Message.Length < unrecognizedBody.Length);
        Assert.Contains("(truncated)", ex.Message);
        Assert.DoesNotContain(new string('x', 300), ex.Message);
    }

    [Fact]
    public async Task ValidationErrorMessageContainingCrLf_IsSanitizedInTheThrownException()
    {
        // Regression test (CWE-117): if MarkMonitor's own validation response ever echoes back a
        // requester-controlled value (e.g. a CSR-derived field) verbatim in its "message" text, an
        // embedded CR/LF must not reach this component's logs unescaped - the same guarantee already
        // applied to every request-side field must hold for this response-derived path too.
        const string maliciousMessage =
            "cert.csr is invalid\r\n2026-08-10 09:00:00 [INF] Enrollment completed successfully for subject: CN=admin.internal";
        var validationBody = $$"""
                              {"validations":[{"field":"cert.csr","code":"field.invalidFormat","message":"{{maliciousMessage.Replace("\r\n", "\\r\\n")}}"}]}
                              """;
        var handler = new FakeHttpMessageHandler()
            .WithSuccessfulAuth()
            .When(req => FakeHttpMessageHandler.Is(req, "GET", "/certs/v1/organization"),
                FakeHttpMessageHandler.Json(HttpStatusCode.BadRequest, validationBody));
        var client = handler.BuildClient();
        await client.AuthenticateAsync();

        var ex = await Assert.ThrowsAsync<Exception>(() => client.ListOrganizationsAsync());

        Assert.DoesNotContain("\r\n", ex.Message, StringComparison.Ordinal);
        Assert.Contains("\\r\\n", ex.Message, StringComparison.Ordinal);
    }
}
