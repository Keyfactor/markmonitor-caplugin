namespace Keyfactor.Extensions.CAPlugin.MarkMonitor;

/// <summary>
/// Escapes CR/LF out of a value before it's interpolated into a log message. Subject/CN (and other
/// CSR-derived fields) are fully controlled by the certificate requester - an embedded CR/LF would
/// otherwise render as real line breaks in a text-based log sink, letting a requester forge a
/// fabricated log entry (CWE-117) that could be mistaken for a genuine, unrelated line by anyone
/// relying on this plugin's logs to reconstruct certificate-issuance history.
/// </summary>
internal static class LogSanitizer
{
    public static string ForLog(string value) => value?.Replace("\r", "\\r").Replace("\n", "\\n");
}
