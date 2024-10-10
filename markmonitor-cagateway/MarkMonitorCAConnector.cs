// using System.Collections.Concurrent;
// using Keyfactor.AnyGateway.Extensions;
// using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
// using Keyfactor.Logging;
// using Keyfactor.PKI.Enums.EJBCA;
// using Microsoft.Extensions.Logging;
// using Newtonsoft.Json;
// using MarkMonitorConstants = Keyfactor.Extensions.CAPlugin.MarkMonitor.Constants;
//
// namespace Keyfactor.Extensions.CAPlugin.MarkMonitor;
//
// public class MarkMonitorCAPlugin : IAnyCAPlugin
// {
//     private readonly ILogger _logger;
//     private ICertificateDataReader _certificateDataReader;
//     private MarkMonitorConfig _config;
//
//     public MarkMonitorCAPlugin()
//     {
//         _logger = LogHandler.GetClassLogger<MarkMonitorCAPlugin>();
//     }
//
//     private Dictionary<int, string> DCVTokens { get; } = new();
//
//     public void Initialize(IAnyCAPluginConfigProvider configProvider, ICertificateDataReader certificateDataReader)
//     {
//         _certificateDataReader = certificateDataReader;
//         var rawConfig = JsonConvert.SerializeObject(configProvider.CAConnectionData);
//         _config = JsonConvert.DeserializeObject<MarkMonitorConfig>(rawConfig);
//     }
//
//     public async Task<AnyCAPluginCertificate> GetSingleRecord(string caRequestID)
//     {
//         var client = await CreateAndAuthenticateClientAsync();
//         return await client.GetSingleCertificateAsync(caRequestID);
//     }
//
//     public async Task Synchronize(BlockingCollection<AnyCAPluginCertificate> blockingBuffer, DateTime? lastSync,
//         bool fullSync, CancellationToken cancelToken)
//     {
//         _logger.MethodEntry();
//
//         try
//         {
//             if (fullSync)
//             {
//                 var client = await CreateAndAuthenticateClientAsync();
//
//                 _logger.LogInformation("Performing a full CA synchronization");
//
//                 // Check for cancellation before the potentially long-running operation
//                 cancelToken.ThrowIfCancellationRequested();
//
//                 var certificates = await client.GetCertificateInventoryAsync(_config.MarkMonitorCaId, "+id", 100,
//                     blockingBuffer, cancelToken);
//                 _logger.LogDebug($"Synchronized {certificates} certificates");
//             }
//
//             // Check for cancellation after operation
//             cancelToken.ThrowIfCancellationRequested();
//         }
//         catch (OperationCanceledException)
//         {
//             _logger.LogInformation("Synchronization canceled.");
//             throw; // Rethrow the cancellation exception to ensure it's propagated
//         }
//         catch (Exception ex)
//         {
//             _logger.LogError($"An error occurred during synchronization: {ex.Message}");
//             throw;
//         }
//         finally
//         {
//             _logger.MethodExit();
//         }
//     }
//
//     public async Task<int> Revoke(string caRequestID, string hexSerialNumber, uint revocationReason)
//     {
//         try
//         {
//             _logger.LogInformation(
//                 $"Revoking certificate with CARequestID: {caRequestID}, SerialNumber: {hexSerialNumber}, Reason: {revocationReason}");
//
//             var client = await CreateAndAuthenticateClientAsync();
//
//             _logger.LogInformation("Performing a Revoke");
//
//             var revokeResult = await client.RevokeCertificateAsync(caRequestID, _config.MarkMonitorCaId);
//
//             if (revokeResult.RevokedAt != null) return (int)EndEntityStatus.REVOKED;
//
//             throw new Exception("Revoke Failed Certificate Was Not Revoked");
//         }
//         catch (Exception e)
//         {
//             throw new Exception($"Revoke Failed with message {e?.Message}");
//         }
//     }
//
//     public async Task<EnrollmentResult> Enroll(string csr, string subject, Dictionary<string, string[]> san,
//         EnrollmentProductInfo productInfo, RequestFormat requestFormat, EnrollmentType enrollmentType)
//     {
//         _logger.LogInformation("Enroll called with CSR: " + csr);
//
//         var client = await CreateAndAuthenticateClientAsync();
//
//         _logger.LogInformation("Performing an Enrollment");
//
//         var enrollResult = await client.EnrollCertificateAsync(csr, productInfo.ProductID, _config.MarkMonitorCaId,
//             Convert.ToInt16(productInfo.ProductParameters["NumberOfDaysValid"]));
//
//         return enrollResult;
//     }
//
//     public async Task Ping()
//     {
//         _logger.MethodEntry();
//         try
//         {
//             _logger.LogInformation("Attempting to authenticate");
//             var client = await CreateAndAuthenticateClientAsync();
//             _logger.LogInformation("Returned from Authenticate Call");
//
//             if (client == null) throw new Exception("Error attempting to ping MarkMonitor");
//             var privs = await client.GetPrivilegesAsync();
//
//             if (privs != null && privs.privileges.Count > 0)
//                 _logger.LogDebug("Successfully pinged MarkMonitor API and found auth privileges.");
//             else
//                 _logger.LogDebug("Could not ping MarkMonitor CA API.");
//         }
//         catch (Exception e)
//         {
//             _logger.LogError($"There was an error contacting MarkMonitor: {e.Message}.");
//             throw new Exception($"Error attempting to ping MarkMonitor: {e.Message}.", e);
//         }
//
//         _logger.MethodExit();
//     }
//
//     public async Task ValidateCAConnectionInfo(Dictionary<string, object> connectionInfo)
//     {
//         _logger.LogInformation("Validation successful");
//
//         var errors = new List<string>();
//
//         _logger.LogTrace("Checking the API Secret.");
//         var apiSecret = connectionInfo.ContainsKey(MarkMonitorConstants.Config.ClientSecret)
//             ? (string)connectionInfo[MarkMonitorConstants.Config.ClientSecret]
//             : string.Empty;
//         if (string.IsNullOrWhiteSpace(apiSecret)) errors.Add("The API Secret is required.");
//
//         _logger.LogTrace("Checking the Base URL.");
//         var baseURL = connectionInfo.ContainsKey(MarkMonitorConstants.Config.BaseUrl)
//             ? (string)connectionInfo[MarkMonitorConstants.Config.BaseUrl]
//             : string.Empty;
//         if (string.IsNullOrWhiteSpace(baseURL))
//             errors.Add("The Base URL is Empty and required.");
//         else if (!baseURL.Contains("http")) errors.Add("The Base URL needs http:// or https://");
//
//         _logger.LogTrace("Checking the API Secret.");
//         var apiClient = connectionInfo.ContainsKey(MarkMonitorConstants.Config.MarkMonitorApiClient)
//             ? (string)connectionInfo[MarkMonitorConstants.Config.MarkMonitorApiClient]
//             : string.Empty;
//         if (string.IsNullOrWhiteSpace(apiClient)) errors.Add("The API Client is required.");
//
//         _logger.LogTrace("Checking the CaId.");
//         var caId = connectionInfo.ContainsKey(MarkMonitorConstants.Config.MarkMonitorCaId)
//             ? (string)connectionInfo[MarkMonitorConstants.Config.MarkMonitorCaId]
//             : string.Empty;
//         if (string.IsNullOrWhiteSpace(apiClient)) errors.Add("The CaId is a required Integer value.");
//
//
//         if (errors.Any()) ThrowValidationException(errors);
//     }
//
//     public Task ValidateProductInfo(EnrollmentProductInfo productInfo, Dictionary<string, object> connectionInfo)
//     {
//         _logger.LogInformation("Product Info validated successfully");
//         return Task.CompletedTask;
//     }
//
//     public Dictionary<string, PropertyConfigInfo> GetCAConnectorAnnotations()
//     {
//         return new Dictionary<string, PropertyConfigInfo>
//         {
//             [MarkMonitorConstants.Config.ClientSecret] = new()
//             {
//                 Comments = "Client Secret for Generating Bearer Token",
//                 Hidden = true,
//                 DefaultValue = "",
//                 Type = "String"
//             },
//             [MarkMonitorConstants.Config.BaseUrl] = new()
//             {
//                 Comments = "Base Url for MarkMonitor API such as https://url:8443",
//                 Hidden = false,
//                 DefaultValue = "",
//                 Type = "String"
//             },
//             [MarkMonitorConstants.Config.MarkMonitorApiClient] = new()
//             {
//                 Comments = "MarkMonitor API Client Name",
//                 Hidden = false,
//                 DefaultValue = "",
//                 Type = "String"
//             },
//             [MarkMonitorConstants.Config.MarkMonitorCaId] =
//                 new() //No Call Available to get a list of the CAs with Ids in API so...
//                 {
//                     Comments =
//                         "MarkMonitor Ca Id.  Example would be 2. In MarkMonitor Onboard UI, click edit on the Ca and look at the id in the Url.",
//                     Hidden = false,
//                     DefaultValue = "",
//                     Type = "String"
//                 }
//         };
//     }
//
//     public Dictionary<string, PropertyConfigInfo> GetTemplateParameterAnnotations()
//     {
//         return new Dictionary<string, PropertyConfigInfo>
//         {
//             [MarkMonitorConstants.ProductParams.NumberOfDaysValid] = new()
//             {
//                 Comments =
//                     "OPTIONAL: The number of days of validity to use when requesting certs. If not provided, default is 365.",
//                 Hidden = false,
//                 DefaultValue = 365,
//                 Type = "Number"
//             }
//         };
//     }
//
//     public List<string> GetProductIds()
//     {
//         return new List<string>
//         {
//             MarkMonitorConstants.Products.Ca,
//             MarkMonitorConstants.Products.CodeSigning,
//             MarkMonitorConstants.Products.Https,
//             MarkMonitorConstants.Products.TlsClient,
//             MarkMonitorConstants.Products.Trusted
//         };
//     }
//
//     private async Task<MarkMonitorClient> CreateAndAuthenticateClientAsync()
//     {
//         var client = new MarkMonitorClient(_config.BaseUrl, _logger);
//         await client.AuthenticateAsync(_config.MarkMonitorApiClient, _config.ClientSecret);
//         return client;
//     }
//
//     private void ThrowValidationException(List<string> errors)
//     {
//         var validationMsg = $"Validation errors:\n{string.Join("\n", errors)}";
//         throw new AnyCAValidationException(validationMsg);
//     }
// }

