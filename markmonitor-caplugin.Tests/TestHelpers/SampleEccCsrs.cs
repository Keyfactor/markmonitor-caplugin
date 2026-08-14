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

/// <summary>Valid ECC (P-256, CN=test.mmcertdomain.com) CSRs differing only in how the key's curve
/// is encoded - named-curve (OID reference) vs. explicit parameters (prime/coefficients/base point
/// spelled out). MarkMonitor accepts the former and silently fails an order using the latter.</summary>
public static class SampleEccCsrs
{
    public const string NamedCurvePem = """
                                         -----BEGIN CERTIFICATE REQUEST-----
                                         MIHYMIGAAgEAMCAxHjAcBgNVBAMMFXRlc3QubW1jZXJ0ZG9tYWluLmNvbTBZMBMG
                                         ByqGSM49AgEGCCqGSM49AwEHA0IABE1FpjBDQ/1D3WBypTFej+WT8TflM8TfiS5v
                                         wHtWf/ss/6XmNYcUp7y6JO0Hkzzn5bSRNUI4qcn3PtkytIXYwugwCgYIKoZIzj0E
                                         AwIDRwAwRAIgROdwnkYWB1+GHDeLPMnRoP5N2SE0m6BpJQO68851t9cCIHpDUMnt
                                         fPNyziHHK8YmUhMlGfNCUa5IjPgRwA51ByDR
                                         -----END CERTIFICATE REQUEST-----
                                         """;

    public const string ExplicitCurvePem = """
                                            -----BEGIN CERTIFICATE REQUEST-----
                                            MIIBtjCCAVwCAQAwIDEeMBwGA1UEAwwVdGVzdC5tbWNlcnRkb21haW4uY29tMIIB
                                            MzCB7AYHKoZIzj0CATCB4AIBATAsBgcqhkjOPQEBAiEA/////wAAAAEAAAAAAAAA
                                            AAAAAAD///////////////8wRAQg/////wAAAAEAAAAAAAAAAAAAAAD/////////
                                            //////wEIFrGNdiqOpPns+u9VXaYhrxlHQawzFOw9jvOPD4n0mBLBEEEaxfR8uEs
                                            Qkf4vOblY6RA8ncDfYEt6zOg9KE5RdiYwpZP40Li/hp/m47n60p8D54WK84zV2sx
                                            Xs7LtkBoN79R9QIhAP////8AAAAA//////////+85vqtpxeehPO5ysL8YyVRAgEB
                                            A0IABEq7nGUCuVXEVLX4xbC3T+mPmMFXSDjpIxscIH5rAMz6Ho6xeXdQroQcn1Am
                                            j3f/2c1XssmutrtIMrgaBox6Tj4wCgYIKoZIzj0EAwIDSAAwRQIgLrjt/SWWn46z
                                            d7J06TvNsE3HXKWK1OLorkJK70daDXICIQCRK/BK5UmBpOLFuenlcIQjSj3B3lbk
                                            33B/in3asgYtcw==
                                            -----END CERTIFICATE REQUEST-----
                                            """;
}
