using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.IntegrationTests;

/// <summary>
/// Cleanup for a real, billable MarkMonitor order created by a live enrollment test. Mirrors
/// TestConsole/Program.cs's CleanUpOrderAsync (cancel, falling back to revoke on failure) - orders
/// created by these tests are freshly submitted and never reach an issued state before this runs.
///
/// Unlike TestConsole (a manual tool with a MARKMONITOR_SKIP_CLEANUP escape hatch for deliberate
/// inspection runs), this suite is meant to run unattended and repeatedly, so there is no opt-out:
/// a cleanup failure here throws (failing the test loudly) rather than just logging a warning,
/// since a silently-failed cleanup would otherwise leave a billable order behind with nothing
/// flagging it for manual follow-up.
/// </summary>
internal static class OrderCleanup
{
    public static async Task CleanUpOrderAsync(MarkMonitorClient client, string orderId)
    {
        try
        {
            await client.CancelCertificateAsync(orderId);
        }
        catch (Exception cancelEx)
        {
            try
            {
                await client.RevokeCertificateAsync(orderId);
            }
            catch (Exception revokeEx)
            {
                throw new Exception(
                    $"Could not cancel or revoke order {orderId} - it may still incur charges and must be cleaned up manually. Cancel error: {cancelEx.Message}; Revoke error: {revokeEx.Message}");
            }
        }
    }
}
