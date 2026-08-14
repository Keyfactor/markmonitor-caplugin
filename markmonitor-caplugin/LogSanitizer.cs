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
