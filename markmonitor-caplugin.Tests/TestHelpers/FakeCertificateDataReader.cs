using Keyfactor.AnyGateway.Extensions;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

public class FakeCertificateDataReader : ICertificateDataReader
{
    public Dictionary<string, string> SerialNumberToRequestId { get; } = new();
    public Dictionary<string, int> RequestIdToStatus { get; } = new();

    public Task<int> GetStatusByRequestID(string caRequestID) =>
        Task.FromResult(RequestIdToStatus.GetValueOrDefault(caRequestID, 0));

    public Task<bool> DoesCertExistForRequestID(string caRequestID) =>
        Task.FromResult(RequestIdToStatus.ContainsKey(caRequestID));

    public Task<string> GetRequestIDBySerialNumber(string serialNumber) =>
        Task.FromResult(SerialNumberToRequestId.GetValueOrDefault(serialNumber, string.Empty));

    public DateTime? GetExpirationDateByRequestId(string caRequestID) => null;
}
