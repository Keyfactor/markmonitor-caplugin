// See https://aka.ms/new-console-template for more information

using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;
using Keyfactor.PKI.PEM;
using Newtonsoft.Json;
using Org.BouncyCastle.Asn1.X509;
using TestConsole.Helpers;

namespace TestConsole;

internal abstract class Program
{
    private const int GenerateCertsCount = 1;

    private static async Task Main(string[] args)
    {
        var baseUrl = Environment.GetEnvironmentVariable("MARKMONITOR_BASE_URL");
        var apiToken = Environment.GetEnvironmentVariable("MARKMONITOR_API_TOKEN");
        var username = Environment.GetEnvironmentVariable("MARKMONITOR_USERNAME");
        var password = Environment.GetEnvironmentVariable("MARKMONITOR_PASSWORD");
        if (string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(apiToken) || string.IsNullOrEmpty(username) ||
            string.IsNullOrEmpty(password))
        {
            Console.WriteLine(
                "Please set MARKMONITOR_API_TOKEN, MARKMONITOR_USERNAME and MARKMONITOR_PASSWORD environment variables.");
            throw new Exception("Invalid test config: missing environment variables.");
        }


        Console.WriteLine("Starting tests...");

        Console.WriteLine("Testing Authenticate...");
        await TestAuthenticate(baseUrl, apiToken, username, password);
        Console.WriteLine("Authenticate test passed.");

        Console.WriteLine("Testing ListOrgs...");
        var orgs = await TestListOrgs(baseUrl, apiToken, username, password);
        if (orgs.Count <= 0) throw new Exception("No organizations found, please add some to run this test.");
        Console.WriteLine("ListOrgs test passed.");

        Console.WriteLine("Testing ListCertificates...");
        var certs = await TestListCertificateOrders(baseUrl, apiToken, username, password);
        if (certs.Count <= 0) throw new Exception("No certificates found, please add some to run this test.");
        Console.WriteLine("ListCertificates test passed.");
        var certsJson = JsonConvert.SerializeObject(certs, Formatting.Indented);
        Console.WriteLine(certsJson);

        var csrGenerator = new CsrGenerator();
        var rsaCsrs = await csrGenerator.GenerateCsrs(1, CsrGenerator.KeyType.RSA);
        var eccCsrs = await csrGenerator.GenerateCsrs(1, CsrGenerator.KeyType.ECC);
        // var dsaCsrs = await csrGenerator.GenerateCsrs(1, CsrGenerator.KeyType.DSA);

        //combine the keypair lists
        rsaCsrs.AddRange(eccCsrs);
        // rsaCsrs.AddRange(dsaCsrs);

        var orders = new List<OrderContent>();
        foreach (var (csr, privateKey, keyPair) in rsaCsrs)
        {
            var csrPem = PemUtilities.DERToPEM(csr.GetEncoded(), PemUtilities.PemObjectType.CertRequest);
            var commonName = csr.GetCertificationRequestInfo().Subject.GetValueList()[0];
            var privateKeyPem = PemUtilities.DERToPEM(privateKey, PemUtilities.PemObjectType.PrivateKey);
            var randomNumberOfEmails = new Random().Next(1, 4);
            var additionalEmails = await EmailAddressGenerator.GenerateRandomEmailsAsync(randomNumberOfEmails);
            var orgId = orgs[0].Id;
            var orgGuid = Guid.Parse(orgId);
            var orgContact = orgs[0].Contacts[0];
            var contactId = orgContact.Id;
            var signatureAlgorithm = csr.SignatureAlgorithm.Algorithm.Id;

            var requestAlgorithm = signatureAlgorithm switch
            {
                //check if algorithm is RSA or ECC
                "1.2.840.113549.1.1.11" => AlgorithmTypes.Rsa.GetDescription(),
                "1.2.840.10045.4.3.1" or "1.2.840.10045.4.3.2" or "1.2.840.10045.4.3.3" or "1.2.840.10045.4.3.4"
                    or "1.2.840.10045.2.1" => AlgorithmTypes.Ecc.GetDescription(),
                // "2.16.840.1.101.3.4.3.1" or "2.16.840.1.101.3.4.3.2" or "2.16.840.1.101.3.4.3.3"
                //     or "2.16.840.1.101.3.4.3.4" => AlgorithmTypes.Dsa.GetDescription(), //DSA not supported
                _ => throw new Exception($"Invalid signature algorithm {signatureAlgorithm}")
            };

            var orderContacts = new List<MarkMonitorCreateOrderContact>
            {
                new()
                {
                    Id = contactId,
                    ContactTypes = orgContact.ContactTypes
                }
            };

            var dcvEmails = new List<DcvEmail>
            {
                new()
                {
                    Email = "justin.mack@markmonitor.com",
                    DnsName = "mmcertdomain.com",
                    EmailDomain = "markmonitor.com"
                }
            };
            var certOrder = new MarkMonitorCreateOrderRequest
            {
                AdditionalEmails = additionalEmails,
                SkipPrice = true,
                OrganizationId = orgGuid,
                // GroupId = null,
                // Contacts = orderContacts,
                Comments = "Requested via Keyfactor Command",
                CertType = CertOrderTypes.SslDvGeotrust.GetDescription(),
                Locale = "en",
                Provider = "DIGICERT",
                Cert = new MarkMonitorOrderRequestCert
                {
                    CommonName = commonName,
                    Csr = csrPem,
                    // ServerPlatform = CertServerPlatforms.Default.GetDescription(),
                    DcvMethod = "EMAIL",
                    DcvEmails = new List<DcvEmail>(),
                    // Provider = "DIGICERT",
                    AlgorithmHash = requestAlgorithm
                }
            };
            Console.WriteLine("Creating certificate order...");

            var client = new MarkMonitorClient(baseUrl, apiToken, username, password);
            await client.AuthenticateAsync();
            Console.WriteLine("Authenticated.");
            var order = await client.CreateCertificateOrder(certOrder);
            Console.WriteLine($"Created certificate order: {order.Id}");
            orders.Add(order);
        }

        Console.WriteLine("Tests completed successfully with orders: " + orders.Count);


        // Console.WriteLine("Testing UpdateCertificate...");
        // var updatedIds = await TestUpdateCertificate(baseUrl, bearerToken);
        // if (updatedIds.Count <= 0) throw new Exception("No certificates updated, please add some to run this test.");
        // Console.WriteLine("UpdateCertificate test passed.");

        // Console.WriteLine("Testing DeleteCertificate...");
        // var deletedIds = await TestDeleteCertificate(baseUrl, bearerToken);
        // if (deletedIds.Count <= 0) throw new Exception("No certificates deleted, please add some to run this test.");

        Console.WriteLine("Tests completed successfully.");
    }

    // private static async Task<List<int>> TestDeleteCertificate(string baseUrl, string bearerToken)
    // {
    //     var client = new AirlockClient(baseUrl);
    //     var deletedCerts = new List<int>();
    //     await client.AuthenticateAsync(bearerToken);
    //     await client.LoadConfigurationAsync(0);
    //
    //     try
    //     {
    //         var currentCerts = await client.ListCertificatesAsync();
    //         if (currentCerts.Count <= 0)
    //             throw new Exception("No certificates found, please add some to run this test.");
    //
    //         foreach (var cert in currentCerts)
    //         {
    //             var certId = cert.Value.Id;
    //             //convert certId from string to int
    //             var certIdInt = int.Parse(certId);
    //
    //             if (certIdInt <= 0)
    //             {
    //                 Console.WriteLine("Invalid certificate ID, skipping delete.");
    //                 continue;
    //             }
    //
    //             var deletedCert = await client.DeleteCertificateAsync(certIdInt);
    //             Console.WriteLine($"Deleted certificate: {deletedCert}");
    //             deletedCerts.Add(certIdInt);
    //             await client.ActivateConfigAsync($"Keyfactor Universal Orchestrator deleted certificate {certIdInt}");
    //         }
    //     }
    //     finally
    //     {
    //         await client.TerminateSessionAsync();
    //     }
    //
    //     return deletedCerts;
    // }
    //
    // private static async Task<List<int>> TestUpdateCertificate(string baseUrl, string bearerToken)
    // {
    //     var client = new AirlockClient(baseUrl);
    //     var updatedCerts = new List<int>();
    //     await client.AuthenticateAsync(bearerToken);
    //     await client.LoadConfigurationAsync(0);
    //
    //     try
    //     {
    //         var currentCerts = await client.ListCertificatesAsync();
    //         if (currentCerts.Count <= 0)
    //             throw new Exception("No certificates found, please add some to run this test.");
    //
    //         foreach (var cert in currentCerts)
    //         {
    //             var certId = cert.Value.Id;
    //             //convert certId from string to int
    //             var certIdInt = int.Parse(certId);
    //
    //             if (certIdInt <= 0)
    //             {
    //                 Console.WriteLine("Invalid certificate ID, skipping update.");
    //                 continue;
    //             }
    //
    //             var generatorWithoutPassphrase = new CertificateGenerator();
    //             var certificatesWithoutPassphrase = await
    //                 generatorWithoutPassphrase.GenerateCertificates(1, CertificateGenerator.KeyType.RSA);
    //
    //             var newCert = certificatesWithoutPassphrase[0];
    //             var certPem =
    //                 PemUtilities.DERToPEM(newCert.certificate.GetEncoded(), PemUtilities.PemObjectType.Certificate);
    //             var privateKeyPem = PemUtilities.DERToPEM(newCert.privateKey, PemUtilities.PemObjectType.PrivateKey);
    //
    //             var certRequest = new AirlockCreateCertRequest
    //             {
    //                 Data = new Data
    //                 {
    //                     Type = "ssl-certificate",
    //                     Attributes = new CreateCertAttributes
    //                     {
    //                         CertType = "SERVER_CERT",
    //                         Certificate = certPem,
    //                         PrivateKey = privateKeyPem,
    //                         RootCaCertificate = certPem,
    //                         CertificateChain = new List<string>(),
    //                         Passphrase = ""
    //                     }
    //                 }
    //             };
    //
    //             Console.WriteLine("Updating certificate with ID: " + certIdInt);
    //             var updatedCert = await client.UpdateCertificateAsync(certIdInt, certRequest);
    //             Console.WriteLine($"Updated certificate: {updatedCert}");
    //             updatedCerts.Add(certIdInt);
    //             await client.ActivateConfigAsync($"Keyfactor Universal Orchestrator updated certificate {certIdInt}");
    //         }
    //     }
    //     finally
    //     {
    //         await client.TerminateSessionAsync();
    //     }
    //
    //     return updatedCerts;
    // }
    //
    // private static async Task TestCreateCertificate(string baseUrl, string bearerToken)
    // {
    //     var client = new AirlockClient(baseUrl);
    //     await client.AuthenticateAsync(bearerToken);
    //     await client.LoadConfigurationAsync(0);
    //
    //     var generatorWithoutPassphrase = new CertificateGenerator();
    //     var certificatesWithoutPassphrase = await
    //         generatorWithoutPassphrase.GenerateCertificates(GenerateCertsCount,
    //             CertificateGenerator.KeyType.RSA);
    //
    //     // create more certificates with no passphrase with ECC key
    //     var generatorWithoutPassphraseECC = new CertificateGenerator();
    //     var certificatesWithoutPassphraseECC = await
    //         generatorWithoutPassphraseECC.GenerateCertificates(GenerateCertsCount,
    //             CertificateGenerator.KeyType.ECC);
    //
    //     // create more certificates with no passphrase with DSA key
    //     var generatorWithoutPassphraseDSA = new CertificateGenerator();
    //     var certificatesWithoutPassphraseDSA = await
    //         generatorWithoutPassphraseDSA.GenerateCertificates(GenerateCertsCount,
    //             CertificateGenerator.KeyType.DSA);
    //
    //     // merge the keypair lists
    //     certificatesWithoutPassphrase.AddRange(certificatesWithoutPassphraseECC);
    //     certificatesWithoutPassphrase.AddRange(certificatesWithoutPassphraseDSA);
    //     // create certificates with no passphrase with RSA key
    //     var certTypes = new List<string> { "SERVER_CERT", "CLIENT_CERT" };
    //     try
    //     {
    //         foreach (var certType in certTypes)
    //         {
    //             Console.WriteLine("Creating certificates for Airlock of type " + certType);
    //
    //             foreach (var (certificate, privateKeyPemStr, _) in certificatesWithoutPassphrase)
    //             {
    //                 Console.WriteLine($"Attempting to create certificate: {certificate}");
    //                 // Console.WriteLine("Unencrypted Private Key: " + Convert.ToBase64String(cert.privateKey));
    //                 // Console.WriteLine("Private Key (Key Pair): " + cert.keyPair.Private);
    //                 var certificateBytes = certificate.GetEncoded();
    //
    //                 var certPem = PemUtilities.DERToPEM(certificateBytes, PemUtilities.PemObjectType.Certificate);
    //                 var privateKeyPem = PemUtilities.DERToPEM(privateKeyPemStr, PemUtilities.PemObjectType.PrivateKey);
    //                 var certRequest = new AirlockCreateCertRequest
    //                 {
    //                     Data = new Data
    //                     {
    //                         Type = "ssl-certificate",
    //                         Attributes = new CreateCertAttributes
    //                         {
    //                             CertType = certType,
    //                             Certificate = certPem,
    //                             PrivateKey = privateKeyPem,
    //                             RootCaCertificate = certPem,
    //                             CertificateChain = new List<string>(),
    //                             Passphrase = ""
    //                         }
    //                     }
    //                 };
    //                 var createdCert = await client.CreateCertificateAsync(certRequest);
    //                 Console.WriteLine($"Created {certType} certificate: {createdCert}");
    //                 var validConfig = await client.ValidateConfigurationAsync();
    //                 if (validConfig)
    //                 {
    //                     Console.WriteLine("Configuration is valid.");
    //                     await client.ActivateConfigAsync(
    //                         $"Keyfactor Universal Orchestrator created certificate {createdCert}");
    //                 }
    //                 else
    //                 {
    //                     Console.WriteLine("Configuration is invalid.");
    //                 }
    //             }
    //         }
    //     }
    //     finally
    //     {
    //         await client.TerminateSessionAsync();
    //     }
    // }
    //
    private static async Task<Dictionary<string, OrderContent>> TestListCertificateOrders(string baseUrl,
        string apiToken, string username, string password)
    {
        var client = new MarkMonitorClient(baseUrl, apiToken, username, password);
        await client.AuthenticateAsync();
        Console.WriteLine("Authenticated.");

        var orders = await client.ListCertificateOrdersAsync(0, "", "", 100);
        var output = new Dictionary<string, OrderContent>();
        foreach (var certificateOrder in orders)
        {
            output.Add(certificateOrder.Id, certificateOrder);
            Console.WriteLine(
                $"Listing certificate {certificateOrder.Cert.CommonName} with ID: {certificateOrder.Id} and {certificateOrder.Cert.DaysRemaining} days remaining.");
        }

        return output;
    }

    private static async Task<List<MarkMonitorOrganizationResponse>> TestListOrgs(string baseUrl, string apiToken,
        string username, string password)
    {
        var client = new MarkMonitorClient(baseUrl, apiToken, username, password);
        await client.AuthenticateAsync();
        Console.WriteLine("Authenticated.");

        var orgs = await client.ListOrganizationsAsync(0, 0);
        foreach (var org in orgs)
        {
            Console.WriteLine($"Org: {org.Id} - {org.Provider} - {org.ProviderId}");
            foreach (var validation in org.Validations)
                Console.WriteLine($"Validation: {validation.Name} - {validation.Type}");
        }

        var orgIds = await client.ListOrganizationsAsync(0, 1, "Markmonitor");

        return orgs;
    }

    private static async Task TestAuthenticate(string baseUrl, string apiToken, string username, string password)
    {
        var client = new MarkMonitorClient(baseUrl, apiToken, username, password);
        await client.AuthenticateAsync();
        Console.WriteLine("Authenticated.");
    }
}