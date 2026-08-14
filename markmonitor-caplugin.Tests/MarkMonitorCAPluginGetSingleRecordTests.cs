using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests;

[Collection(LogHandlerFactoryCollection.Name)]
public class MarkMonitorCAPluginGetSingleRecordTests
{
    [Fact]
    public async Task GetSingleRecord_WithCaRequestIdContainingCrLf_SanitizesItInLogOutput()
    {
        // Regression test (CWE-117): GetSingleRecord() used to log the caller-supplied caRequestId
        // verbatim before the GUID validation performed deeper inside MarkMonitorClient ever ran,
        // letting an embedded CR/LF forge a fake log line.
        using var _ = CapturingLoggerFactory.Install(out var capturingFactory);

        const string maliciousCaRequestId = "not-a-guid\r\n2026-08-10 09:00:00 [INF] FAKE forged log line";
        var handler = new FakeHttpMessageHandler().WithSuccessfulAuth();
        var plugin = new MarkMonitorCAPlugin(handler.BuildClient());
        plugin.Initialize(FakeAnyCAPluginConfigProvider.WithDefaults(), new FakeCertificateDataReader());

        await Assert.ThrowsAsync<ArgumentException>(() => plugin.GetSingleRecord(maliciousCaRequestId));

        Assert.DoesNotContain(capturingFactory.Messages, m => m.Contains("\r\n", StringComparison.Ordinal));
        Assert.Contains(capturingFactory.Messages, m => m.Contains("\\r\\n", StringComparison.Ordinal));
    }
}
