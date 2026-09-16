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

using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

/// <summary>
///     Represents a request to reissue a certificate.
/// </summary>
public class MarkMonitorReissueRequest
{
    /// <summary>
    ///     Gets or sets the certificate details.
    /// </summary>
    [JsonProperty("cert")]
    public MarkMonitorCertificate Cert { get; set; }

    /// <summary>
    ///     Gets or sets a value indicating whether to ignore organization check.
    /// </summary>
    [JsonProperty("ignoreOrgCheck")]
    public bool IgnoreOrgCheck { get; set; }

    /// <summary>
    ///     Gets or sets the additional emails associated with the request.
    /// </summary>
    [JsonProperty("additionalEmails")]
    public List<string> AdditionalEmails { get; set; }
}