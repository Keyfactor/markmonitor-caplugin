using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;
using Keyfactor.Logging;
using Keyfactor.PKI.Enums.EJBCA;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Certificate = Keyfactor.Extensions.CAPlugin.MarkMonitor.Models.MarkMonitorCertificate;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;

public class MarkMonitorClient
{
    private readonly string _baseUrl;
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private string _bearerToken;
    private string _apiKey;
    private string _username;
    private string _password;

    public string BaseUrl => _baseUrl;

    public MarkMonitorClient(string baseUrl, string apiKey, string username, string password, bool validateSsl = true)
    {
        _baseUrl = baseUrl;
        _logger = LogHandler.GetClassLogger(GetType());
        _apiKey = apiKey;
        _username = username;
        _password = password;
        
        var handler = new HttpClientHandler { UseCookies = false };

        if (!validateSsl)
        {
            _logger.LogWarning("SSL certificate validation is disabled for {BaseUrl}", baseUrl);
            handler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
        }


        _httpClient = new HttpClient(handler);
        _ = AuthenticateAsync();
    }

    private bool ValidateConfiguration()
    {
        if (string.IsNullOrEmpty(_apiKey))
            throw new ConfigurationValidationException("API Key is required.");

        if (string.IsNullOrEmpty(_username))
            throw new ConfigurationValidationException("Username is required.");

        if (string.IsNullOrEmpty(_password))
            throw new ConfigurationValidationException("Password is required.");

        return true;
    }

    public async Task AuthenticateAsync(string apiKey = null, string username = null, string password = null)
    {
        _logger.MethodEntry();
        if (!string.IsNullOrEmpty(apiKey))
        {
            _logger.LogDebug("Setting API Key");
            _apiKey = apiKey;
        }

        if (!string.IsNullOrEmpty(username))
        {
            _logger.LogDebug("Setting username");
            _username = username;
        }

        if (!string.IsNullOrEmpty(password))
        {
            _logger.LogDebug("Setting password");
            _password = password;
        }
        
        _logger.LogDebug("Calling ValidateConfiguration");
        var isValid = ValidateConfiguration();
        if (!isValid) throw new ConfigurationValidationException("Invalid configuration");
        
        _logger.LogDebug("Setting \"X-API-KEY\" header");
        _httpClient.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);
        var requestBody = new TokenRequest
        {
            Username = _username,
            Password = _password
        };

        var requestUrl = $"{_baseUrl}/auth/v1/auth/authenticate";
        _logger.LogInformation("Authenticating with MarkMonitor API at {RequestUrl}", requestUrl);
        
        _logger.LogDebug("Sending authentication request");
        var response = await _httpClient.PostAsync(requestUrl,
            new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json"));

        _logger.LogDebug("Reading authentication response");
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        _logger.LogTrace("Authentication response code: {ResponseCode}", response.StatusCode);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Deserializing token response");
            var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(content);
            _bearerToken = tokenResponse.BearerToken;
            _logger.LogDebug("Bearer token received and valid for {TokenExpiration} seconds",
                tokenResponse.ExpiresIn);
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _bearerToken);
        }
        else
        {
            var errMsg = BuildErrorString(content);
            _logger.LogError("Authentication failed: {EMessage}", errMsg);
            throw new Exception(errMsg);
        }
        _logger.LogInformation("Authentication successful");
    }

    public async Task<int> GetCertificateInventoryAsync(string caId, string sort, int limit, BlockingCollection<AnyCAPluginCertificate> certificatesBuffer, CancellationToken cancelToken)
        {
            try
            {
                var certificateOrders = await ListCertificateOrdersAsync(0, caId, sort, limit); //todo: providerId support???
                var numberOfCertificates = 0;
                foreach (var certificateDetail in certificateOrders)
                {
                    certificatesBuffer.Add(
                        new AnyCAPluginCertificate
                        {
                            CARequestID = certificateDetail.Id,
                            Certificate = certificateDetail.Cert.EndEntityCert,
                            Status = MarkMonitorCertificateStatusToCAStatus(certificateDetail),
                            ProductID = certificateDetail.CertType,
                            RevocationDate = Convert.ToDateTime(certificateDetail.Cert.DaysRemaining)
                        }, cancelToken);
                    numberOfCertificates++;
                }
                return numberOfCertificates;
            }
            catch (OperationCanceledException)
            {
                _logger.LogInformation("Certificate inventory retrieval cancelled");
                throw; // Rethrow the cancellation exception to ensure it's propagated
            }
            catch (Exception e)
            {
                _logger.LogError("An error has occurred: {EMessage}", e.Message);
                return 0;
            }
            finally
            {
                certificatesBuffer.CompleteAdding(); // Ensure buffer is completed even on cancellation
            }
        }

    private List<string> BuildQueryString(int providerId, string orgId, string sort, int limit, int page)
    {
        _logger.MethodEntry();
        var query = new List<string>();
        if (page > 0) query.Add($"page={page}");
        if (limit > 0) query.Add($"size={limit}");
        if (!string.IsNullOrEmpty(sort)) query.Add($"sort={sort}");
        if (providerId > 0) query.Add($"providerId={providerId}");
        if (!string.IsNullOrEmpty(orgId)) query.Add($"organizationId={orgId}");
        _logger.MethodExit();
        return query;
    }

    public async Task<List<OrderContent>> ListCertificateOrdersAsync(int providerId, string orgId, string sort,
        int limit)
    {
        _logger.MethodEntry();
        EnsureAuthenticated();
        var output = new List<OrderContent>();
        try
        {
            _logger.LogInformation("Retrieving certificate orders from MarkMonitor");
            var currentPage = 0;
            var allPagesDownloaded = false;

            do
            {
                var nextUrl =
                    $"{_baseUrl}/certs/v1/order";
                _logger.LogTrace("Base URL: {BaseUrl}", nextUrl);
                
                _logger.LogDebug("Building query string");
                var query = BuildQueryString(providerId, orgId, sort, limit, currentPage);
                if (query.Count > 0) nextUrl += "?" + string.Join("&", query);

                _logger.LogTrace("Getting page \'{CurrentPage}\' of \'{Limit}\')", currentPage, limit);

                _logger.LogDebug("Getting certificate orders from MarkMonitor {NextUrl}", nextUrl);
                var response = await _httpClient.GetAsync(nextUrl); // Pass the token here

                _logger.LogDebug("Reading response content");
                var content = await response.Content.ReadAsStringAsync();
                
                if (!response.IsSuccessStatusCode) throw new Exception(BuildErrorString(content));

                _logger.LogDebug("Deserializing response content to MarkMonitorListOrdersResponse");
                var certificateListResponse = JsonConvert.DeserializeObject<MarkMonitorListOrdersResponse>(content);
                output.AddRange(certificateListResponse.Content);
                currentPage++;
                allPagesDownloaded = currentPage >= certificateListResponse.Page.TotalPages;
            } while (!allPagesDownloaded);
            return output;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Certificate inventory retrieval cancelled");
            throw; // Rethrow the cancellation exception to ensure it's propagated
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred: {EMessage}", e.Message);
            return null;
        }
    }

    public async Task<AnyCAPluginCertificate> GetSingleCertificateAsync(string certificateId)
    {
        return null;
        // try
        // {
        //     EnsureAuthenticated();
        //
        //     _httpClient.DefaultRequestHeaders.Authorization =
        //         new AuthenticationHeaderValue("Bearer", _bearerToken);
        //     _httpClient.DefaultRequestHeaders.Accept.Add(
        //         new MediaTypeWithQualityHeaderValue("application/json"));
        //
        //     var response = await _httpClient.GetAsync($"{_baseUrl}/api/certificate/{certificateId}");
        //     var content = await response.Content.ReadAsStringAsync();
        //     Certificate cert;
        //     if (response.IsSuccessStatusCode)
        //         cert = JsonConvert.DeserializeObject<Certificate>(content);
        //     else
        //         throw new Exception(BuildErrorString(content));
        //
        //     return new AnyCAPluginCertificate
        //     {
        //         CARequestID = cert.Id.ToString(),
        //         Certificate = cert.CertificatePem,
        //         Status = MarkMonitorCertificateStatusToCAStatus(cert),
        //         ProductID = cert.CertificateType,
        //         RevocationDate = cert.RevokedAt != null ? Convert.ToDateTime(cert.RevokedAt) : null
        //     };
        // }
        // catch (Exception e)
        // {
        //     _logger.LogError($"An error has occurred: {e.Message}");
        //     return null;
        // }
    }

    public async Task<EnrollmentResult> EnrollCertificateAsync(string csr, string productId, string caId,
        short numberOfDaysValid)
    {
        // try
        // {
        //     EnsureAuthenticated();
        //
        //     var requestBody = new EnrollCertificateRequest
        //     {
        //         Csr = csr,
        //         CaId = Convert.ToInt16(caId),
        //         CertType = productId,
        //         CsrEncoding = "text",
        //         IssueCert = true,
        //         Days = numberOfDaysValid
        //     };
        //
        //     _httpClient.DefaultRequestHeaders.Authorization =
        //         new AuthenticationHeaderValue("Bearer", _bearerToken);
        //     _httpClient.DefaultRequestHeaders.Accept.Add(
        //         new MediaTypeWithQualityHeaderValue("application/json"));
        //
        //     var response = await _httpClient.PostAsync($"{_baseUrl}/api/certificate/request",
        //         new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json"));
        //     var content = await response.Content.ReadAsStringAsync();
        //     if (response.IsSuccessStatusCode)
        //     {
        //         var certificate = JsonConvert.DeserializeObject<Certificate>(content);
        //         return new EnrollmentResult
        //         {
        //             CARequestID = certificate.Id.ToString(),
        //             Certificate = certificate.CertificatePem,
        //             Status = MarkMonitorCertificateStatusToCAStatus(certificate),
        //             StatusMessage = "Certificate enrolled successfully"
        //         };
        //     }
        //
        //     throw new Exception(BuildErrorString(content));
        // }
        // catch (Exception e)
        // {
        //     _logger.LogError($"An error has occurred: {e.Message}");
        //     return null;
        // }
        return null;
    }

    private async Task<dynamic> UpdateOrder(string certificateId, string action, dynamic payload)
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Revoking certificate {CertificateId}", certificateId);
            EnsureAuthenticated();

            var url = $"{_baseUrl}/certs/v1/order/{certificateId}/{action}";
            _logger.LogDebug("{Action}ing certificate at {Url}", action, url);
            var response = await _httpClient.PatchAsync(url, payload);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Certificate {CertificateId} has been {action}ed", certificateId, action);
                return content;
            }
            
            var errMsg = BuildErrorString(content);
            _logger.LogError("An error has occurred while attempting to revoke {CertificateId}", certificateId);
            throw new Exception(errMsg);
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred while attempting to revoke {CertificateId}: {EMessage}", certificateId, e.Message);
            throw;
        }
    } 
    public async Task<bool> RevokeCertificateAsync(string certificateId, string caId)
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Revoking certificate {CertificateId}", certificateId);
            EnsureAuthenticated();

            var url = $"{_baseUrl}/certs/v1/order/{certificateId}/revoke";
            _logger.LogDebug("Revoking certificate at {Url}", url);
            var payload = new StringContent("", Encoding.UTF8, "application/json");
            var response = await _httpClient.PatchAsync(url, payload);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Certificate {CertificateId} has been revoked", certificateId);
                return true;
            }
            
            var errMsg = BuildErrorString(content);
            _logger.LogError("An error has occurred while attempting to revoke {CertificateId}: {EMessage}", certificateId, errMsg);
            throw new Exception(errMsg);
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred while attempting to revoke {CertificateId}: {EMessage}", certificateId, e.Message);
            throw;
        }
    }

    private void EnsureAuthenticated()
    {
        if (string.IsNullOrEmpty(_bearerToken))
        {
            AuthenticateAsync().RunSynchronously();
        }
    }

    private static string BuildErrorString(string jsonString)
    {
        var json = JObject.Parse(jsonString);
        var errorMessages = new List<string>();

        if (json["validation_messages"] != null)
            foreach (var validationMessage in json["validation_messages"])
            {
                var field = validationMessage.Path;
                var fieldErrors = (JObject)validationMessage.First;

                foreach (var error in fieldErrors)
                {
                    var errorMessage = error.Value.ToString();
                    errorMessages.Add($"{field}: {errorMessage}");

                    if (error.Key == "options")
                    {
                        var options = string.Join(", ", error.Value.ToObject<List<string>>());
                        errorMessages.Add($"{field} options: {options}");
                    }
                }
            }
        else if (json["detail"] != null)
            errorMessages.Add(json["detail"].ToString());
        else
            return "No validation errors found.";

        return string.Join(Environment.NewLine, errorMessages);
    }

    private int MarkMonitorCertificateStatusToCAStatus(OrderContent order)
    {
        _logger.MethodEntry();
        if (order == null || string.IsNullOrEmpty(order.Status))
        {
            _logger.LogError("MarkMonitor order is null or status is empty");
            return (int)EndEntityStatus.FAILED;
        }

        
        _logger.LogDebug("MarkMonitor order {OrderId} status: {OrderStatus}", order.Id, order.Status);
        if (
            order.Status.Equals(OrderStatus.DigiPending.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.DigiProcessing.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.DigiReissuePending.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.DigiWaitingPickup.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.ReissuePending.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.ReissueRequestPending.GetDescription(), StringComparison.OrdinalIgnoreCase)
        )
        {
            _logger.LogDebug("MarkMonitor order {OrderId} status resolved to 'IN PROCESS'", order.Id);
            return (int)EndEntityStatus.INPROCESS;
        }

        if (
            order.Status.Equals(OrderStatus.DigiRevoked.GetDescription(), StringComparison.OrdinalIgnoreCase)
        )
        {
            _logger.LogDebug("MarkMonitor order {OrderId} status resolved to 'REVOKED'", order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.REVOKED;
        }


        if (order.Status.Equals(OrderStatus.DigiIssued.GetDescription(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogDebug("MarkMonitor order {OrderId} status resolved to 'GENERATED'", order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.GENERATED;
        }


        if (
            order.Status.Equals(OrderStatus.DigiFailed.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.DigiReissueFailed.GetDescription(), StringComparison.OrdinalIgnoreCase)
            )
        {
            _logger.LogDebug("MarkMonitor order {OrderId} status resolved to 'FAILED'", order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.FAILED;
        }


        if (
            order.Status.Equals(OrderStatus.DigiCanceled.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.DigiRejected.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.DigiExpired.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.DigiNeedsCsr.GetDescription(), StringComparison.OrdinalIgnoreCase)
        )
        {
            _logger.LogDebug("MarkMonitor order {OrderId} status resolved to 'CANCELLED'", order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.CANCELLED;
        }


        if (
            order.Status.Equals(OrderStatus.Created.GetDescription(), StringComparison.OrdinalIgnoreCase)
        )
        {
            
            _logger.LogDebug("MarkMonitor order {OrderId} status resolved to 'INITIALIZED'", order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.INITIALIZED;
        }
        
        _logger.LogError("MarkMonitor order {OrderId} status could not be resolved defaulting to 'FAILED'", order.Id);
        _logger.MethodExit();
        return (int)EndEntityStatus.FAILED;
    }
    //
    // private static int MarkMonitorCertificateStatusToCAStatus(Certificate cert)
    // {
    //     if (cert.RevokedAt != null) return (int)EndEntityStatus.REVOKED;
    //
    //     if (cert.HasCertificate && cert.IsValid) return (int)EndEntityStatus.GENERATED;
    //
    //     return (int)EndEntityStatus.FAILED;
    // }
}

public class ConfigurationValidationException : Exception
{
    public ConfigurationValidationException()
    {
    }

    public ConfigurationValidationException(string message)
        : base(message)
    {
    }

    public ConfigurationValidationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}