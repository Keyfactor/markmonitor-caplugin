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

using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;

namespace TestConsole.Helpers;

public class CsrGenerator
{
    public enum KeyType
    {
        RSA,
        DSA,
        ECC
    }

    public async Task<List<(Pkcs10CertificationRequest csr, byte[] privateKey, AsymmetricCipherKeyPair keyPair)>>
        GenerateCsrs(int numCsrs, KeyType keyType, string? passphrase = null)
    {
        var csrs = new List<(Pkcs10CertificationRequest, byte[], AsymmetricCipherKeyPair)>();
        for (var i = 0; i < numCsrs; i++)
        {
            var randomDN = await DistinguishedNameGenerator.GenerateRandomDNAsync();
            var csr = GenerateCsr(keyType, randomDN, passphrase);
            csrs.Add(csr);
        }

        return csrs;
    }

    private (Pkcs10CertificationRequest csr, byte[] privateKey, AsymmetricCipherKeyPair keyPair)
        GenerateCsr(KeyType keyType, string distinguishedName, string? passphrase = null)
    {
        // Generate the key pair based on the chosen key type
        var keyPair = GenerateKeyPair(keyType);

        // Create the CSR generator
        var subjectDN = new X509Name(distinguishedName);
        var signatureFactory = CreateSignatureFactory(keyType, keyPair.Private);
        var signatureAlgorithm = GetSignatureAlgorithm(keyType);

        // Generate the CSR
        var csr = new Pkcs10CertificationRequest(
            signatureAlgorithm,
            subjectDN,
            keyPair.Public,
            null,
            keyPair.Private);

        // Return encrypted or unencrypted private key based on whether a passphrase is provided
        var privateKey = string.IsNullOrEmpty(passphrase)
            ? ExportPrivateKeyUnencrypted(keyPair.Private) // No passphrase, unencrypted
            : EncryptPrivateKey(keyPair.Private, passphrase); // Passphrase provided, encrypt private key

        return (csr, privateKey, keyPair);
    }

    // Helper method to map the key type to the appropriate signature algorithm
    private string GetSignatureAlgorithm(KeyType keyType)
    {
        return keyType switch
        {
            KeyType.RSA => "SHA256WITHRSA",
            KeyType.DSA => "SHA256WITHDSA",
            KeyType.ECC => "SHA256WITHECDSA",
            _ => throw new ArgumentException("Unsupported key type")
        };
    }

    private static ISignatureFactory CreateSignatureFactory(KeyType keyType, AsymmetricKeyParameter privateKey)
    {
        return keyType switch
        {
            KeyType.RSA => new Asn1SignatureFactory("SHA256WithRSA", privateKey),
            KeyType.DSA => new Asn1SignatureFactory("SHA256WithDSA", privateKey),
            KeyType.ECC => new Asn1SignatureFactory("SHA256WithECDSA", privateKey),
            _ => throw new ArgumentException("Unsupported key type")
        };
    }

    private static AsymmetricCipherKeyPair GenerateKeyPair(KeyType keyType)
    {
        return keyType switch
        {
            KeyType.RSA => GenerateRsaKeyPair(),
            KeyType.DSA => GenerateDsaKeyPair(),
            KeyType.ECC => GenerateEccKeyPair(),
            _ => throw new ArgumentException("Unsupported key type")
        };
    }

    private static AsymmetricCipherKeyPair GenerateRsaKeyPair()
    {
        var keyPairGen = new RsaKeyPairGenerator();
        keyPairGen.Init(new KeyGenerationParameters(new SecureRandom(new CryptoApiRandomGenerator()), 2048));
        return keyPairGen.GenerateKeyPair();
    }

    private static AsymmetricCipherKeyPair GenerateDsaKeyPair()
    {
        var keyPairGen = new DsaKeyPairGenerator();
        var dsaParamGen = new DsaParametersGenerator();
        dsaParamGen.Init(1024, 80, new SecureRandom());
        var dsaParams = dsaParamGen.GenerateParameters();
        var keyGenParams = new DsaKeyGenerationParameters(new SecureRandom(), dsaParams);
        keyPairGen.Init(keyGenParams);
        return keyPairGen.GenerateKeyPair();
    }

    private static AsymmetricCipherKeyPair GenerateEccKeyPair()
    {
        var keyPairGen = new ECKeyPairGenerator();
        var ecSpec = SecNamedCurves.GetByName("secp256r1"); // P-256 curve
        // ECNamedDomainParameters (not plain ECDomainParameters) is required so the CSR's
        // SubjectPublicKeyInfo references the named curve by OID rather than spelling out explicit
        // curve parameters (prime/coefficients/base point) - CA/Browser Forum baseline requirements
        // disallow explicit EC parameters for publicly-trusted certs, and DigiCert silently rejects
        // such a CSR (order fails almost instantly, with no reason surfaced via MarkMonitor's API).
        var ecDomainParams = new ECNamedDomainParameters(SecObjectIdentifiers.SecP256r1, ecSpec);
        var keyGenParams =
            new ECKeyGenerationParameters(ecDomainParams, new SecureRandom(new CryptoApiRandomGenerator()));
        keyPairGen.Init(keyGenParams);
        return keyPairGen.GenerateKeyPair();
    }

    // Function to encrypt the private key using a passphrase
    private static byte[] EncryptPrivateKey(AsymmetricKeyParameter privateKey, string? passphrase)
    {
        var pkcs8Gen = new Pkcs8Generator(privateKey, Pkcs8Generator.PbeSha1_3DES)
        {
            Password = passphrase?.ToCharArray()
        };

        using var ms = new MemoryStream();
        var pemWriter = new PemWriter(new StreamWriter(ms));
        pemWriter.WriteObject(pkcs8Gen);
        pemWriter.Writer.Flush();
        return ms.ToArray();
    }

    // Function to export the private key without encryption
    private static byte[] ExportPrivateKeyUnencrypted(AsymmetricKeyParameter privateKey)
    {
        using var ms = new MemoryStream();
        var pemWriter = new PemWriter(new StreamWriter(ms));
        pemWriter.WriteObject(privateKey);
        pemWriter.Writer.Flush();
        return ms.ToArray();
    }
}