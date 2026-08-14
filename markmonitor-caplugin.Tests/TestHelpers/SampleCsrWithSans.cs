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

using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Tests.TestHelpers;

/// <summary>Generates a fresh RSA CSR with a CSR-embedded SAN (subjectAltName) extension request,
/// for tests exercising the union of dictionary-supplied and CSR-embedded SANs. Unlike
/// <see cref="SampleCsr"/>'s fixed PEM, this is generated on the fly since the fixed fixtures
/// predate SAN support and none of them carry an extension request.</summary>
public static class SampleCsrWithSans
{
    public static string GeneratePem(string commonName, params string[] dnsNames)
    {
        var keyPairGen = new RsaKeyPairGenerator();
        keyPairGen.Init(new KeyGenerationParameters(new SecureRandom(), 2048));
        var keyPair = keyPairGen.GenerateKeyPair();

        var subject = new X509Name($"CN={commonName}");
        var generalNames = new GeneralNames(dnsNames.Select(d => new GeneralName(GeneralName.DnsName, d)).ToArray());
        var extensions = new X509Extensions(new Dictionary<DerObjectIdentifier, X509Extension>
        {
            [X509Extensions.SubjectAlternativeName] = new X509Extension(false, new DerOctetString(generalNames))
        });
        var attributes = new DerSet(new AttributePkcs(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest,
            new DerSet(extensions)));

        var csr = new Pkcs10CertificationRequest("SHA256WITHRSA", subject, keyPair.Public, attributes,
            keyPair.Private);

        using var stream = new MemoryStream();
        using var writer = new StreamWriter(stream);
        new PemWriter(writer).WriteObject(csr);
        writer.Flush();
        return System.Text.Encoding.ASCII.GetString(stream.ToArray());
    }
}
