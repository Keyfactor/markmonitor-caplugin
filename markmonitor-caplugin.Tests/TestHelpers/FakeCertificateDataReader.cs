using Keyfactor.AnyGateway.Extensions;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

public class FakeCertificateDataReader : ICertificateDataReader
{
    public Dictionary<string, string> SerialNumberToRequestId { get; } = new();
    public Dictionary<string, int> RequestIdToStatus { get; } = new();

    /// <summary>Request IDs for which a lookup call throws, simulating a downstream failure (e.g. a
    /// transient database error) for one specific record during a sync.</summary>
    public HashSet<string> ThrowForRequestIds { get; } = new();

    public Task<int> GetStatusByRequestID(string caRequestID)
    {
        if (ThrowForRequestIds.Contains(caRequestID))
            throw new Exception($"Simulated lookup failure for {caRequestID}");
        return Task.FromResult(RequestIdToStatus.GetValueOrDefault(caRequestID, 0));
    }

    public Task<bool> DoesCertExistForRequestID(string caRequestID)
    {
        if (ThrowForRequestIds.Contains(caRequestID))
            throw new Exception($"Simulated lookup failure for {caRequestID}");
        return Task.FromResult(RequestIdToStatus.ContainsKey(caRequestID));
    }

    public Task<string> GetRequestIDBySerialNumber(string serialNumber) =>
        Task.FromResult(SerialNumberToRequestId.GetValueOrDefault(serialNumber, string.Empty));

    public Dictionary<string, DateTime> ExpirationDateByRequestId { get; } = new();

    public DateTime? GetExpirationDateByRequestId(string caRequestID) =>
        ExpirationDateByRequestId.TryGetValue(caRequestID, out var expiration) ? expiration : null;
}
