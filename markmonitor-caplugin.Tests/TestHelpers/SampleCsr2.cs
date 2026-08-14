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

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

/// <summary>A second, distinct valid RSA CSR (CN=other.mmcertdomain.com) for tests that need two
/// different CSRs, e.g. to confirm enrollment idempotency is scoped correctly.</summary>
public static class SampleCsr2
{
    public const string Pem = """
                              -----BEGIN CERTIFICATE REQUEST-----
                              MIICZjCCAU4CAQAwITEfMB0GA1UEAwwWb3RoZXIubW1jZXJ0ZG9tYWluLmNvbTCC
                              ASIwDQYJKoZIhvcNAQEBBQADggEPADCCAQoCggEBAKbI0/Flsz3CyIIVQlVd95HM
                              MG1iVSLTsC+TDmIP08UQB8EWQu7gmC4fbKu3ip5dtKEn7hVMVveQohNMxbFNPZKv
                              6TtmjizB9JD4sx/JpFNr0UJg9I55b09dZ/XeeewemUO+fodLhBxsEz0aU9tjqxkh
                              RXs4Ts84jhtXZ597yzsxPxzqMif1+ARh2uq9dOok/FCRNXisz9yI+WWOW89BiS3e
                              QnOVnrsh6eX7xwDbP4rz8nTf7g1daY3INruluT0g2gdJGPTThIu3haocnUTtEjkn
                              2Qn4f0Wxg/GXckR1igNYHKIew927p7SxIqxZnWMH4L/YkAuguG3RwvWA+VzOnWUC
                              AwEAAaAAMA0GCSqGSIb3DQEBCwUAA4IBAQBTd2VIhGxwRpfS7mXiU3pumvY6TLib
                              dw18ALi57VFxo95ZAyIdD+t68ISt6sFF5e7TTV6Fy9uqpnwFGCYS9yXdbS7/VDjj
                              Xvz2JmZO1q+mYT85/bahvRLvX0mlZhViucu4ryUy5BgUEVyrLk4QiLn9U1fgiA87
                              l/2NVswRTovqsK6pFrcsVT1nqqkbywDKgBXQD5RB8WFPeKpM+qlz4R/yx6jvlJ3a
                              5Sa8oP/kr//U+chmQiTopPFA20Hx/uKD+geO02qgb+ThdHMnJp+QeZ1lkdMrxkLP
                              CvrDLqOiLH1B2+4ImzBVbg2UzcnolwSeeQUR78bUNhxE/Pifn6lFgyrc
                              -----END CERTIFICATE REQUEST-----
                              """;
}
