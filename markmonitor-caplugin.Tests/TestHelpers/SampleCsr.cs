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

/// <summary>A fixed, valid RSA CSR (CN=test.mmcertdomain.com) for enrollment tests.</summary>
public static class SampleCsr
{
    public const string Pem = """
                              -----BEGIN CERTIFICATE REQUEST-----
                              MIICZTCCAU0CAQAwIDEeMBwGA1UEAwwVdGVzdC5tbWNlcnRkb21haW4uY29tMIIB
                              IjANBgkqhkiG9w0BAQEFAAOCAQ8AMIIBCgKCAQEAne7uDpjrruy7z2sv+RKbSW4U
                              erEOKO8DLd2H4/6x0NzWW0sMEZ9Z/vHx1JYgYHjoS1UIHPg30tBtL6/CZPuC3j00
                              bcWbyBhEosrcfOb7xX5lKScNO3qvlZE9EGcY/kCl2m/2kvn0Jric8WIar8zbxe8f
                              A7VUA4DcGZUZTNfrmEGw3xVKvEOZQBH7OIUsSKSsdifzcldiwvRb2xiCcMd8hLp3
                              XGMEU+9o6pZM8PYU3SWT1KPAsp29Uef1l4u4e4SreerIoNV1NgmCShpj8zf0lHp+
                              JlocXOzxicf+2njnzBYYEXWkDoPBGLzKT6ShJR2MejpKMO8qviLfGP2jIN427QID
                              AQABoAAwDQYJKoZIhvcNAQELBQADggEBAGHPpJi5OqAnDmIJ3+i2HMeObiCddax0
                              hBWeoEje2B2o2M+twsXDtmSUxp5CmZTT4SrJeft9jsH10ZG5cd7ypMR3SKYMBmAP
                              n3a8xGOQKODOaO1KUbZyJ4fxePXHHw6QrTx9AKrWEV9Y19K8kIgdnafqRiyqJ10r
                              uyKih/NvUzC0ETWXCCcGYP5BI2S+kEnT5r9osqYlTMTJwGiTuoutUkW1VB/o7SU1
                              8A0FAvkClJGY5DNOJ5AMTKdD6E5X+09b9YuDUAhqg+ivYPCBsluYDS3Ayc0qZpCD
                              OjqDSP9B1HX7+OIZRgHNQ7aiHocPlzqBzcW9M+byDjbL6sZHJtKf6mQ=
                              -----END CERTIFICATE REQUEST-----
                              """;
}
