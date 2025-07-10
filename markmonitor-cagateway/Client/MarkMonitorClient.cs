using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;
using Keyfactor.Logging;
using Keyfactor.PKI.Enums.EJBCA;
using Keyfactor.PKI.PEM;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Tls;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;

public class MarkMonitorClient
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private string _apiKey;
    private string _bearerToken;
    private string _password;
    private string _username;

    public MarkMonitorClient(string baseUrl, string apiKey, string username, string password, bool validateSsl = true)
    {
        BaseUrl = baseUrl;
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
        // _ = AuthenticateAsync();
    }

    public string BaseUrl { get; }

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

        var requestUrl = $"{BaseUrl}/auth/v1/auth/authenticate";
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

    public async Task<int> GetCertificateInventoryAsync(string caId, string sort, int limit,
        BlockingCollection<AnyCAPluginCertificate> certificatesBuffer, CancellationToken cancelToken)
    {
        _logger.MethodEntry();
        try
        {
            EnsureAuthenticated();
            _logger.LogInformation("Retrieving certificate inventory from MarkMonitor");
            var certificateOrders =
                await ListCertificateOrdersAsync(0, caId, sort, limit); //todo: providerId support???
            _logger.LogDebug("Retrieved '{CertificateCount}' certificate orders", certificateOrders.Count);

            var numberOfCertificates = 0;
            foreach (var certificateDetail in certificateOrders)
            {
                _logger.LogInformation("Adding certificate {CertificateId} to buffer", certificateDetail.Id);
                var certStatus = MarkMonitorCertificateStatusToCAStatus(certificateDetail);
                _logger.LogTrace("Certificate {CertificateId} status: {CertificateStatus}", certificateDetail.Id,
                    certStatus);

                _logger.LogDebug("Converting certificate {CertificateId} revocation status {Status}",
                    certificateDetail.Id, certificateDetail.Cert.RevokeStatus);
                DateTime? revocationDate = null;
                if (certificateDetail.Cert.RevokeStatus == "REVOKED")
                {
                    _logger.LogDebug("Certificate {CertificateId} is revoked", certificateDetail.Id);
                    revocationDate = Convert.ToDateTime(certificateDetail.Cert.DateValidUntil);
                }
                
                var fullChain = new StringBuilder();
                if (certificateDetail.Cert.EndEntityCert != null)
                {
                    _logger.LogDebug("Adding end entity certificate to full chain for {CertificateId}",
                        certificateDetail.Id);
                    fullChain.AppendLine(certificateDetail.Cert.EndEntityCert);
                }
                if (certificateDetail.Cert.IntermediateCert != null)
                {
                    _logger.LogDebug("Adding issuer certificate to full chain for {CertificateId}",
                        certificateDetail.Id);
                    fullChain.AppendLine(certificateDetail.Cert.IntermediateCert);
                }
                if (certificateDetail.Cert.RootCert != null)
                {
                    _logger.LogDebug("Adding root certificate to full chain for {CertificateId}",
                        certificateDetail.Id);
                    fullChain.AppendLine(certificateDetail.Cert.RootCert);
                }

                certificatesBuffer.Add(
                    new AnyCAPluginCertificate
                    {
                        CARequestID = certificateDetail.Id,
                        Status = certStatus,
                        Certificate = fullChain.ToString(),
                        CSR = certificateDetail.Cert.Csr,
                        ProductID = certificateDetail.CertType,
                        RevocationDate = revocationDate,
                        // RevocationReason = certificateDetail.Cert.RevokeStatus, // TODO: Not available in MarkMonitor API
                    }, cancelToken);
                numberOfCertificates++;
                _logger.LogTrace("Total certificates added to buffer: {NumberOfCertificates}", numberOfCertificates);
            }

            _logger.LogInformation("Retrieved {NumberOfCertificates} certificates", numberOfCertificates);
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
            _logger.MethodExit();
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

    private List<string> BuildListOrgsQueryString(string name = "", string sort = "", int limit = 0, int page = 0)
    {
        _logger.MethodEntry();
        var query = new List<string>();
        if (page > 0) query.Add($"page={page}");
        if (limit > 0) query.Add($"size={limit}");
        if (!string.IsNullOrEmpty(sort)) query.Add($"sort={sort}");
        if (!string.IsNullOrEmpty(name)) query.Add($"name={name}");

        _logger.LogTrace("Query string: {Query}", query);
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
                    $"{BaseUrl}/certs/v1/order";
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
                allPagesDownloaded = currentPage >= certificateListResponse.MarkMonitorPage.TotalPages;
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
        finally
        {
            _logger.MethodExit();
        }
    }

    public async Task<List<MarkMonitorOrganizationResponse>> ListOrganizationsAsync(int page = 0, int limit = 0,
        string name = "")
    {
        _logger.MethodEntry();
        EnsureAuthenticated();
        var output = new List<MarkMonitorOrganizationResponse>();
        try
        {
            _logger.LogInformation("Retrieving organizations from MarkMonitor");
            var currentPage = 0;
            var allPagesDownloaded = false;

            do
            {
                var nextUrl =
                    $"{BaseUrl}/certs/v1/organization";

                _logger.LogTrace("Base URL: {BaseUrl}", nextUrl);

                _logger.LogDebug("Building query string");
                var query = BuildListOrgsQueryString(name, "", limit, currentPage);
                if (query.Count > 0) nextUrl += "?" + string.Join("&", query);

                _logger.LogTrace("Getting page \'{CurrentPage}\' of \'{Limit}\')", currentPage, limit);

                _logger.LogDebug("Getting organizations from MarkMonitor {NextUrl}", nextUrl);
                var response = await _httpClient.GetAsync(nextUrl); // Pass the token here

                _logger.LogDebug("Reading response content");
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode) throw new Exception(BuildErrorString(content));

                _logger.LogDebug("Deserializing response content to MarkMonitorListOrgResponse");
                var orgListResponse = JsonConvert.DeserializeObject<MarkMonitorListOrgsResponse>(content);

                _logger.LogDebug("Adding organizations to output");
                output.AddRange(orgListResponse.Content);
                currentPage++;
                _logger.LogTrace("Total organizations added to output: {OutputCount}", output.Count);
                allPagesDownloaded = currentPage >= orgListResponse.MarkMonitorPage.TotalPages;
            } while (!allPagesDownloaded);

            return output;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Organization retrieval cancelled");
            throw; // Rethrow the cancellation exception to ensure it's propagated
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred: {EMessage}", e.Message);
            return null;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    public async Task<AnyCAPluginCertificate> GetSingleOrderAsync(string orderId)
    {
        try
        {
            EnsureAuthenticated();

            _httpClient.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue("Bearer", _bearerToken);
            _httpClient.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await _httpClient.GetAsync($"{BaseUrl}/certs/v1/order/{orderId}");
            var content = await response.Content.ReadAsStringAsync();
            OrderContent order;
            if (response.IsSuccessStatusCode)
                order = JsonConvert.DeserializeObject<OrderContent>(content);
            else
                throw new Exception(BuildErrorString(content));

            return new AnyCAPluginCertificate
            {
                CARequestID = order.Id,
                Certificate = order.Cert.EndEntityCert,
                Status = MarkMonitorCertificateStatusToCAStatus(order),
                ProductID = order.CertType,
                RevocationDate = Convert.ToDateTime(order.Cert.DaysRemaining)
            };
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred: {EMessage}", e.Message);
            return null;
        }
    }

    private string getCsrAlgorithm(Pkcs10CertificationRequest csr)
    {
        var signatureAlgorithm = csr.SignatureAlgorithm.Algorithm.Id;
        var requestAlgorithm = signatureAlgorithm switch
        {
            //check if algorithm is RSA or ECC
            "1.2.840.113549.1.1.11" => AlgorithmTypes.Rsa.GetDescription(),
            "1.2.840.10045.4.3.1" or "1.2.840.10045.4.3.2" or "1.2.840.10045.4.3.3" or "1.2.840.10045.4.3.4"
                or "1.2.840.10045.2.1" => AlgorithmTypes.Ecc.GetDescription(),
            // "2.16.840.1.101.3.4.3.1" or "2.16.840.1.101.3.4.3.2" or "2.16.840.1.101.3.4.3.3"
            //     or "2.16.840.1.101.3.4.3.4" => AlgorithmTypes.Dsa.GetDescription(), //DSA not supported
            _ => throw new Exception($"Invalid CSR signature algorithm {signatureAlgorithm}")
        };
        return requestAlgorithm;
    }

    public async Task<EnrollmentResult> EnrollCertificateAsync(string csr, string subject,
        Dictionary<string, string[]> san, string orderType, Dictionary<string, string> productParams,
        MarkMonitorConfig config)
    {
        _logger.MethodEntry();
        try
        {
            EnsureAuthenticated();

            var additionalEmails =
                productParams.GetValueOrDefault("additionalEmails");
            var additionalEmailsList = new List<string>();
            if (!string.IsNullOrEmpty(additionalEmails))
            {
                _logger.LogTrace("Additional emails provided: {AdditionalEmails}", additionalEmails);
                additionalEmails = additionalEmails.Replace(" ", ",");
                additionalEmailsList = additionalEmails.Split(',').ToList();
            }

            var orgIds = await ListOrganizationsAsync(0, 1, config.OrgName);
            _logger.LogTrace("Organizations found: {@OrgIds}", orgIds);
            var orgId = orgIds.FirstOrDefault()?.Id;
            _logger.LogTrace("Organization ID: {OrgId}", orgId);
            if (string.IsNullOrEmpty(orgId))
            {
                _logger.LogError("Organization ID not found for {OrgName}", config.OrgName);
                throw new InvalidDataException($"Organization ID '{config.OrgName}' not found");
            }

            var orgIdGuid = Guid.Parse(orgId);

            var comments = productParams.GetValueOrDefault("comments", "Requested via Keyfactor Command");
            _logger.LogTrace("Comments: {Comments}", comments);
            var locale = productParams.GetValueOrDefault("locale", "en");
            _logger.LogTrace("Locale: {Locale}", locale);
            var provider = productParams.GetValueOrDefault("provider", "DIGICERT");
            _logger.LogTrace("Provider: {Provider}", provider);

            // Lookup order type in CertOrderTypes enum
            _logger.LogDebug("Looking up order type {OrderType} in CertOrderTypes enum", orderType);
            var certOrderType = Enum.Parse<CertOrderTypes>(orderType);

            _logger.LogDebug("Deserializing CSR");
            _logger.LogTrace("CSR: {Csr}", csr);
            var csrObject = new Pkcs10CertificationRequest(GetCsrBytes(csr));
            var csrInfo = csrObject.GetCertificationRequestInfo();

            _logger.LogDebug("Determining CSR algorithm");
            var requestAlgorithm = getCsrAlgorithm(csrObject);
            _logger.LogTrace("CSR algorithm: {RequestAlgorithm}", requestAlgorithm);

            _logger.LogDebug("Converting CSR to PEM");
            var csrPem = PemUtilities.DERToPEM(csrObject.GetEncoded(), PemUtilities.PemObjectType.CertRequest);
            _logger.LogTrace("CSR PEM: {CsrPem}", csrPem);

            _logger.LogDebug("Constructing certificate order object");
            var certOrder = new MarkMonitorCreateOrderRequest
            {
                AdditionalEmails = additionalEmailsList,
                SkipPrice = true,
                OrganizationId = orgIdGuid,
                // GroupId = null,
                // Contacts = orderContacts,
                Comments = comments,
                CertType = certOrderType.GetDescription(),
                Locale = locale,
                Provider = provider,
                Cert = new MarkMonitorOrderRequestCert
                {
                    CommonName = cleanSubject(subject),
                    Csr = csrPem.Replace("\r", ""),
                    DcvMethod = "EMAIL",
                    DcvEmails = new List<DcvEmail>(),
                    AlgorithmHash = requestAlgorithm
                }
            };


            _logger.LogDebug("Calling CreateCertificateOrder");
            logCreateOrderRequest(certOrder);
            var order = await CreateCertificateOrder(certOrder);

            if (order == null) throw new Exception($"Failed to enroll certificate `{subject}` with MarkMonitor");

            _logger.LogInformation("Certificate enrolled successfully");
            return new EnrollmentResult
            {
                CARequestID = order.Id,
                Certificate = order.Cert?.EndEntityCert,
                Status = MarkMonitorCertificateStatusToCAStatus(order),
                StatusMessage = "MarkMonitor order status: " + order.Status
            };
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred: {EMessage}", e.Message);
            return null;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    private string cleanSubject(string subject)
    {
        _logger.MethodEntry();
        try
        {
            // Search for the CN field in the Subject
            var cnPrefix = "CN=";
            var cnIndex = subject.IndexOf(cnPrefix, StringComparison.Ordinal);
            if (cnIndex < 0) return subject;
            var cnStart = cnIndex + cnPrefix.Length;
            var cnEnd = subject.IndexOf(",", cnStart, StringComparison.Ordinal);
            if (cnEnd < 0) cnEnd = subject.Length;
            return subject.Substring(cnStart, cnEnd - cnStart);
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    private void logCreateOrderRequest(MarkMonitorCreateOrderRequest request)
    {
        _logger.MethodEntry();
        _logger.LogTrace("CommonName: {CommonName}", request.Cert.CommonName);
        _logger.LogTrace("OrganizationId: {OrganizationId}", request.OrganizationId);
        _logger.LogTrace("CertType: {CertType}", request.CertType);
        _logger.LogTrace("Locale: {Locale}", request.Locale);
        _logger.LogTrace("Provider: {Provider}", request.Provider);
        _logger.LogTrace("Comments: {Comments}", request.Comments);
        _logger.LogTrace("AdditionalEmails: {AdditionalEmails}", request.AdditionalEmails);
        _logger.LogTrace("SkipPrice: {SkipPrice}", request.SkipPrice);
        _logger.LogTrace("CSR: {Csr}", request.Cert.Csr);
        _logger.LogTrace("Cert: {@Cert}", request.Cert);
        _logger.MethodExit();
    }

    public async Task<OrderContent> CreateCertificateOrder(MarkMonitorCreateOrderRequest request)
    {
        _logger.MethodEntry();
        try
        {
            EnsureAuthenticated();
            logCreateOrderRequest(request);

            var url = $"{BaseUrl}/certs/v1/order";
            _logger.LogDebug("Creating certificate order at {Url}", url);
            var jsonPayload = new StringContent(
                JsonConvert.SerializeObject(request),
                Encoding.UTF8, "application/json"
            );
            _logger.LogTrace("Create order payload: {@Payload}", jsonPayload);
            _logger.LogTrace("Request JSON: {Json}", request.JSONString());
            Console.WriteLine(jsonPayload.ReadAsStringAsync());
            var response = await _httpClient.PostAsync(url, jsonPayload);
            _logger.LogTrace("Response: {Response}", response);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogTrace("Response content: {Content}", content);
            if (response.IsSuccessStatusCode)
            {
                var order = JsonConvert.DeserializeObject<OrderContent>(content);
                _logger.LogInformation("Certificate order {OrderId} created", order.Id);
                return order;
            }

            var errMsg = BuildErrorString(content);
            _logger.LogError("An error has occurred while attempting to create order: {EMessage}", errMsg);
            throw new Exception(errMsg);
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred while attempting to create order: {EMessage}", e.Message);
            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    public async Task<bool> CancelCertificateAsync(string orderId)
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Revoking certificate {CertificateId}", orderId);
            EnsureAuthenticated();

            var url = $"{BaseUrl}/certs/v1/order/{orderId}/cancel";
            _logger.LogDebug("Revoking certificate at {Url}", url);
            var payload = new StringContent("", Encoding.UTF8, "application/json");
            var response = await _httpClient.PatchAsync(url, payload);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Certificate {CertificateId} has been revoked", orderId);
                return true;
            }

            var errMsg = BuildErrorString(content);
            _logger.LogError("An error has occurred while attempting to cancel order {CertificateId}: {EMessage}",
                orderId, errMsg);
            throw new Exception(errMsg);
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred while attempting to cancel {CertificateId}: {EMessage}", orderId,
                e.Message);
            throw;
        }
    }

    public async Task<bool> ReissueCertificateAsync(string orderId, MarkMonitorReissueRequest payload)
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Revoking certificate {CertificateId}", orderId);
            EnsureAuthenticated();

            var url = $"{BaseUrl}/certs/v1/order/{orderId}/reissue";
            _logger.LogDebug("Reissuing certificate at {Url}", url);
            //convert payload to json
            var jsonPayload = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8, "application/json"
            );
            _logger.LogTrace("Reissue payload: {@Payload}", jsonPayload);
            var response = await _httpClient.PatchAsync(url, jsonPayload);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Certificate {CertificateId} has been reissued", orderId);
                return true;
            }

            var errMsg = BuildErrorString(content);
            _logger.LogError("An error has occurred while attempting to reissue {CertificateId}: {EMessage}", orderId,
                errMsg);
            throw new Exception(errMsg);
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred while attempting to reissue {CertificateId}: {EMessage}", orderId,
                e.Message);
            throw;
        }
    }

    public async Task<bool> RevokeCertificateAsync(string orderId, string orgName = null, uint reason = 0)
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Revoking certificate associated with order {OrderId}", orderId);
            EnsureAuthenticated();

            var url = $"{BaseUrl}/certs/v1/order/{orderId}/revoke";
            _logger.LogDebug("Revoking certificate at {Url}", url);
            var payload = new StringContent("", Encoding.UTF8, "application/json");
            var response = await _httpClient.PatchAsync(url, payload);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Certificate {CertificateId} has been revoked", orderId);
                return true;
            }

            var errMsg = BuildErrorString(content);
            _logger.LogError("An error has occurred while attempting to revoke {CertificateId}: {EMessage}", orderId,
                errMsg);
            throw new Exception(errMsg);
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred while attempting to revoke {CertificateId}: {EMessage}", orderId,
                e.Message);
            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    private void EnsureAuthenticated()
    {
        if (string.IsNullOrEmpty(_bearerToken)) AuthenticateAsync().RunSynchronously();
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

    private byte[] GetCsrBytes(string csr)
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogDebug("Attempting to decode CSR string");
            // Try to decode the string from Base64
            return Convert.FromBase64String(csr);
        }
        catch (FormatException)
        {
            _logger.LogDebug("Decoding failed, assuming PEM format");
            // If decoding fails, assume the string is in PEM format
            var pem = csr.Replace("-----BEGIN CERTIFICATE REQUEST-----", "")
                .Replace("-----END CERTIFICATE REQUEST-----", "")
                .Replace("\n", "")
                .Replace("\r", "")
                .Trim();
            return Convert.FromBase64String(pem);
        }
        finally
        {
            _logger.MethodExit();
        }
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
            order.Status.Equals(OrderStatus.DigiNeedsApproval.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.ReissueRequestPending.GetDescription(), StringComparison.OrdinalIgnoreCase)
        )
        {
            _logger.LogInformation("MarkMonitor order {OrderId} status 'IN PROCESS'", order.Id);
            _logger.LogInformation(
                "MarkMonitor order {OrderId} may still be in process and/or require manual intervention", order.Id);
            return (int)EndEntityStatus.INPROCESS;
        }

        if (
            order.Status.Equals(OrderStatus.DigiRevoked.GetDescription(), StringComparison.OrdinalIgnoreCase)
        )
        {
            _logger.LogInformation("MarkMonitor order {OrderId} status 'REVOKED'", order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.REVOKED;
        }


        if (order.Status.Equals(OrderStatus.DigiIssued.GetDescription(), StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("MarkMonitor order {OrderId} status 'GENERATED'", order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.GENERATED;
        }


        if (
            order.Status.Equals(OrderStatus.DigiFailed.GetDescription(), StringComparison.OrdinalIgnoreCase) ||
            order.Status.Equals(OrderStatus.DigiReissueFailed.GetDescription(), StringComparison.OrdinalIgnoreCase)
        )
        {
            _logger.LogError("MarkMonitor order {OrderId} status 'FAILED'", order.Id);
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
            _logger.LogInformation("MarkMonitor order {OrderId} status 'CANCELLED'", order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.CANCELLED;
        }


        if (
            order.Status.Equals(OrderStatus.Created.GetDescription(), StringComparison.OrdinalIgnoreCase)
        )
        {
            _logger.LogInformation("MarkMonitor order {OrderId} status 'INITIALIZED'", order.Id);
            _logger.LogInformation(
                "MarkMonitor order {OrderId} may still be in process and/or require manual intervention", order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.INITIALIZED;
        }

        _logger.LogError("MarkMonitor order {OrderId} status could not be dettermined defaulting to 'FAILED'",
            order.Id);
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