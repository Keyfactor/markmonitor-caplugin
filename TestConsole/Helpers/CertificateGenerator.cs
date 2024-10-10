using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Prng;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;

namespace TestConsole.Helpers;

public class CertificateGenerator
{
    public enum KeyType
    {
        RSA,
        DSA,
        ECC
    }

    public async Task<List<(X509Certificate certificate, byte[] privateKey, AsymmetricCipherKeyPair keyPair)>>
        GenerateCertificates(
            int numCertificates, KeyType keyType, string? passphrase = null)
    {
        var certificates = new List<(X509Certificate, byte[], AsymmetricCipherKeyPair)>();
        for (var i = 0; i < numCertificates; i++)
        {
            var randomDN = await DistinguishedNameGenerator.GenerateRandomDNAsync();
            var certificate = GenerateSelfSignedCertificate(keyType, randomDN, passphrase);
            certificates.Add(certificate);
        }

        return certificates;
    }

    private (X509Certificate certificate, byte[] privateKey, AsymmetricCipherKeyPair keyPair)
        GenerateSelfSignedCertificate(KeyType keyType, string distinguishedName, string? passphrase = null)
    {
        // Generate the key pair based on the chosen key type
        var keyPair = GenerateKeyPair(keyType);

        // Create the certificate generator
        var certGen = new X509V3CertificateGenerator();

        // Define the certificate's distinguished name (DN)
        var subjectDN = new X509Name(distinguishedName);
        var subjectCN = subjectDN.GetValueList(X509Name.CN)[0];

        // Define the certificate's serial number
        var serialNumber = BigInteger.ProbablePrime(120, new Random());

        // Set the validity period (1 year from now)
        var startDate = DateTime.UtcNow;
        var expiryDate = startDate.AddYears(1);

        // Configure the generator
        certGen.SetSerialNumber(serialNumber);
        certGen.SetIssuerDN(subjectDN);
        certGen.SetNotBefore(startDate);
        certGen.SetNotAfter(expiryDate);
        certGen.SetSubjectDN(subjectDN);
        certGen.SetPublicKey(keyPair.Public);

        var ipSans = GenerateRandomIpAddresses(3);

        // add dns subject alternative names
        var subjectAlternativeNames = new List<string>
        {
            TitleCaseConverter.ToTitleCase(subjectCN.Replace(".", " "))
        };
        certGen.AddExtension(X509Extensions.SubjectAlternativeName, false, new DerSequence(
            subjectAlternativeNames.ConvertAll(cn => new GeneralName(GeneralName.DnsName, cn)).ToArray()));

        // Add extensions (Optional)
        certGen.AddExtension(X509Extensions.BasicConstraints, true, new BasicConstraints(true));

        // Add DNS SAN extension (Optional)
        certGen.AddExtension(X509Extensions.SubjectAlternativeName, false, new GeneralNames(
            new GeneralName[]
            {
                new(GeneralName.DnsName, subjectCN)
            }));

        // Add IP SAN extension (Optional)
        certGen.AddExtension(X509Extensions.SubjectAlternativeName, false, new GeneralNames(
            ipSans.ConvertAll(ip => new GeneralName(GeneralName.IPAddress, ip)).ToArray()));

        // Use ContentSigner for compatibility with modern BouncyCastle versions
        var signatureFactory = CreateSignatureFactory(keyType, keyPair.Private);

        // Generate the certificate
        var certificate = certGen.Generate(signatureFactory);

        // Return encrypted or unencrypted private key based on whether a passphrase is provided
        var privateKey = string.IsNullOrEmpty(passphrase)
            ? ExportPrivateKeyUnencrypted(keyPair.Private) // No passphrase, unencrypted
            : EncryptPrivateKey(keyPair.Private, passphrase); // Passphrase provided, encrypt private key

        return (certificate, privateKey, keyPair);
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
        var ecDomainParams = new ECDomainParameters(ecSpec.Curve, ecSpec.G, ecSpec.N, ecSpec.H);
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

    private static List<string> GenerateRandomIpAddresses(int count)
    {
        var ipAddresses = new List<string>();
        for (var i = 0; i < count; i++) ipAddresses.Add(GenerateRandomIpAddress());

        return ipAddresses;
    }

    private static string GenerateRandomIpAddress()
    {
        var rand = new Random();
        return $"{rand.Next(256)}.{rand.Next(256)}.{rand.Next(256)}.{rand.Next(256)}";
    }
}