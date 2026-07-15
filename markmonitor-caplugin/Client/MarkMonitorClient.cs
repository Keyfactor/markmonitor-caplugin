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

public class MarkMonitorClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger _logger;
    private string _apiKey;
    private string _bearerToken;
    private DateTime? _tokenExpiresAtUtc;
    private string _password;
    private string _username;
    private readonly TimeProvider _timeProvider;

    public MarkMonitorClient(string baseUrl, string apiKey, string username, string password, bool validateSsl = true,
        HttpMessageHandler handler = null, TimeProvider timeProvider = null)
    {
        BaseUrl = baseUrl;
        _logger = LogHandler.GetClassLogger(GetType());
        _apiKey = apiKey;
        _username = username;
        _password = password;
        _timeProvider = timeProvider ?? TimeProvider.System;

        // A caller-supplied handler (e.g. a fake in tests) is used as-is; otherwise build the real
        // HttpClientHandler with the usual SSL validation behavior.
        if (handler == null)
        {
            var httpClientHandler = new HttpClientHandler { UseCookies = false };

            if (!validateSsl)
            {
                _logger.LogWarning("SSL certificate validation is disabled for {BaseUrl}", baseUrl);
                httpClientHandler.ServerCertificateCustomValidationCallback = (message, cert, chain, errors) => true;
            }

            handler = httpClientHandler;
        }

        _httpClient = new HttpClient(handler);
    }

    public void Dispose()
    {
        _httpClient.Dispose();
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
        var content = await response.Content.ReadAsStringAsync();

        _logger.LogTrace("Authentication response code: {ResponseCode}", response.StatusCode);
        if (response.IsSuccessStatusCode)
        {
            _logger.LogDebug("Deserializing token response");
            var tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(content);
            _bearerToken = tokenResponse.BearerToken;
            // Subtract a small safety buffer so a request that starts just before expiry doesn't
            // race the token dying mid-flight.
            _tokenExpiresAtUtc = _timeProvider.GetUtcNow().UtcDateTime.AddSeconds(tokenResponse.ExpiresIn - 30);
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
            await EnsureAuthenticatedAsync();
            _logger.LogInformation("Retrieving certificate inventory from MarkMonitor");
            var certificateOrders =
                await ListCertificateOrdersAsync(0, caId, sort, limit); //todo: providerId support???
            _logger.LogDebug("Retrieved '{CertificateCount}' certificate orders", certificateOrders.Count);

            var numberOfCertificates = 0;
            foreach (var certificateDetail in certificateOrders)
            {
                _logger.LogInformation("Adding certificate {CertificateId} to buffer", certificateDetail.Id);

                if (certificateDetail.Cert == null)
                {
                    _logger.LogWarning(
                        "Certificate {CertificateId} has no cert details yet (status {Status}) - skipping it for this sync rather than aborting the rest of the page",
                        certificateDetail.Id, certificateDetail.Status);
                    continue;
                }

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
            throw;
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
        await EnsureAuthenticatedAsync();
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
            throw;
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
        await EnsureAuthenticatedAsync();
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
            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    public async Task<MarkMonitorOrganizationResponse> GetOrganizationAsync(string orgId)
    {
        _logger.MethodEntry();
        try
        {
            await EnsureAuthenticatedAsync();
            var url = $"{BaseUrl}/certs/v1/organization/{orgId}";
            _logger.LogDebug("Getting organization from MarkMonitor {Url}", url);
            var response = await _httpClient.GetAsync(url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) throw new Exception(BuildErrorString(content));

            return JsonConvert.DeserializeObject<MarkMonitorOrganizationResponse>(content);
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

    public async Task<List<MarkMonitorGroup>> ListGroupsAsync(int page = 0, int limit = 0, string name = "")
    {
        _logger.MethodEntry();
        await EnsureAuthenticatedAsync();
        var output = new List<MarkMonitorGroup>();
        try
        {
            _logger.LogInformation("Retrieving groups from MarkMonitor");
            var currentPage = 0;
            var allPagesDownloaded = false;

            do
            {
                var nextUrl = $"{BaseUrl}/auth/v1/group";

                var query = BuildListOrgsQueryString(name, "", limit, currentPage);
                if (query.Count > 0) nextUrl += "?" + string.Join("&", query);

                _logger.LogDebug("Getting groups from MarkMonitor {NextUrl}", nextUrl);
                var response = await _httpClient.GetAsync(nextUrl);

                var content = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode) throw new Exception(BuildErrorString(content));

                var groupListResponse = JsonConvert.DeserializeObject<MarkMonitorListGroupsResponse>(content);
                output.AddRange(groupListResponse.Groups ?? new List<MarkMonitorGroup>());
                currentPage++;
                allPagesDownloaded = currentPage >= groupListResponse.MarkMonitorPage.TotalPages;
            } while (!allPagesDownloaded);

            return output;
        }
        catch (Exception e)
        {
            // Group resolution is an optional, best-effort lookup (EnrollCertificateAsync falls back
            // to no group on failure) - deliberately swallowed rather than failing the enrollment.
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
            await EnsureAuthenticatedAsync();

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

            DateTime? revocationDate = null;
            if (order.Cert.RevokeStatus == "REVOKED") revocationDate = Convert.ToDateTime(order.Cert.DateValidUntil);

            return new AnyCAPluginCertificate
            {
                CARequestID = order.Id,
                Certificate = order.Cert.EndEntityCert,
                Status = MarkMonitorCertificateStatusToCAStatus(order),
                ProductID = order.CertType,
                RevocationDate = revocationDate
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
            await EnsureAuthenticatedAsync();

            var caseInsensitiveParams = new Dictionary<string, string>(productParams, StringComparer.OrdinalIgnoreCase);

            var additionalEmails =
                caseInsensitiveParams.GetValueOrDefault("additionalEmails");
            var additionalEmailsList = new List<string>();
            if (!string.IsNullOrEmpty(additionalEmails))
            {
                _logger.LogTrace("Additional emails provided: {AdditionalEmails}", additionalEmails);
                additionalEmails = additionalEmails.Replace(" ", ",");
                additionalEmailsList = additionalEmails.Split(',').ToList();
            }

            MarkMonitorOrganizationResponse org;
            if (Guid.TryParse(config.OrgName, out _))
            {
                _logger.LogDebug("OrgId '{OrgName}' looks like a GUID - fetching the organization directly",
                    config.OrgName);
                org = await GetOrganizationAsync(config.OrgName);
            }
            else
            {
                var orgs = await ListOrganizationsAsync(0, 1, config.OrgName);
                _logger.LogTrace("Organizations found: {@Orgs}", orgs);
                org = orgs?.FirstOrDefault();
            }

            var orgId = org?.Id;
            _logger.LogTrace("Organization ID: {OrgId}", orgId);
            if (string.IsNullOrEmpty(orgId))
            {
                _logger.LogError("Organization ID not found for {OrgName}", config.OrgName);
                throw new InvalidDataException($"Organization ID '{config.OrgName}' not found");
            }

            var orgIdGuid = Guid.Parse(orgId);

            var comments = caseInsensitiveParams.GetValueOrDefault("comments", "Requested via Keyfactor Command");
            _logger.LogTrace("Comments: {Comments}", comments);
            var locale = caseInsensitiveParams.GetValueOrDefault("locale", "en");
            _logger.LogTrace("Locale: {Locale}", locale);
            var provider = caseInsensitiveParams.GetValueOrDefault("provider", "DIGICERT");
            _logger.LogTrace("Provider: {Provider}", provider);

            _logger.LogDebug("Resolving MarkMonitor contact for order");
            var contactParam = caseInsensitiveParams.GetValueOrDefault(
                MarkMonitorCAPluginConfig.EnrollmentConfigConstants.MarkmonitorContact);
            var resolvedContact = ResolveContact(org?.Contacts, contactParam);
            var orderContacts = resolvedContact != null
                ? new List<MarkMonitorCreateOrderContact>
                {
                    new() { Id = resolvedContact.Id, ContactTypes = resolvedContact.ContactTypes }
                }
                : new List<MarkMonitorCreateOrderContact>();
            if (resolvedContact == null)
                _logger.LogWarning(
                    "No MarkMonitor contact could be resolved for organization {OrgName}; MarkMonitor may reject the order if a contact is required",
                    config.OrgName);
            else
                _logger.LogTrace("Resolved MarkMonitor contact: {ContactId} ({Email})", resolvedContact.Id,
                    resolvedContact.Email);

            _logger.LogDebug("Resolving MarkMonitor group for order");
            var groupParam = caseInsensitiveParams.GetValueOrDefault(
                MarkMonitorCAPluginConfig.EnrollmentConfigConstants.MarkmonitorGroup);
            Guid? groupIdGuid = null;
            if (!string.IsNullOrWhiteSpace(groupParam))
            {
                if (Guid.TryParse(groupParam, out var parsedGroupId))
                {
                    groupIdGuid = parsedGroupId;
                }
                else
                {
                    var groups = await ListGroupsAsync(0, 0, groupParam);
                    var matchedGroup = groups?.FirstOrDefault(g =>
                        string.Equals(g.Name, groupParam, StringComparison.OrdinalIgnoreCase));
                    if (matchedGroup != null)
                        groupIdGuid = Guid.Parse(matchedGroup.Id);
                    else
                        _logger.LogWarning("MarkMonitor group '{GroupParam}' could not be resolved to an ID",
                            groupParam);
                }
            }

            var validDcvMethods = Enum.GetValues<DomainControlValidationMethods>()
                .Select(m => m.GetDescription()).ToList();
            var dcvMethod = caseInsensitiveParams.GetValueOrDefault(
                MarkMonitorCAPluginConfig.EnrollmentConfigConstants.DCVMethod,
                DomainControlValidationMethods.Email.GetDescription());
            if (!validDcvMethods.Contains(dcvMethod, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Invalid DCVMethod '{DcvMethod}' specified, defaulting to EMAIL", dcvMethod);
                dcvMethod = DomainControlValidationMethods.Email.GetDescription();
            }

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
                GroupId = groupIdGuid,
                Contacts = orderContacts,
                Comments = comments,
                CertType = certOrderType.GetDescription(),
                Locale = locale,
                Provider = provider,
                Cert = new MarkMonitorOrderRequestCert
                {
                    CommonName = cleanSubject(subject),
                    Csr = csrPem.Replace("\r", ""),
                    DcvMethod = dcvMethod,
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
            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    private MarkMonitorGetContactResponse ResolveContact(List<MarkMonitorGetContactResponse> contacts,
        string contactParam)
    {
        if (contacts == null || contacts.Count == 0) return null;

        if (!string.IsNullOrWhiteSpace(contactParam))
        {
            if (Guid.TryParse(contactParam, out var contactGuid))
            {
                var byId = contacts.FirstOrDefault(c => c.Id == contactGuid);
                if (byId != null) return byId;
            }

            var byEmail =
                contacts.FirstOrDefault(c => string.Equals(c.Email, contactParam, StringComparison.OrdinalIgnoreCase));
            if (byEmail != null) return byEmail;

            var byName = contacts.FirstOrDefault(c =>
                string.Equals($"{c.FirstName} {c.LastName}", contactParam, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;

            _logger.LogWarning(
                "MarkMonitor contact '{ContactParam}' could not be resolved; falling back to the default organization contact",
                contactParam);
        }

        return contacts.FirstOrDefault(c =>
                   c.ContactTypes != null && c.ContactTypes.Any(t =>
                       string.Equals(t.Type, "ORGANIZATION_CONTACT", StringComparison.OrdinalIgnoreCase)))
               ?? contacts.FirstOrDefault();
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
            await EnsureAuthenticatedAsync();
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
            await EnsureAuthenticatedAsync();

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
            await EnsureAuthenticatedAsync();

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

    /// <summary>
    /// Revokes the certificate for the given order. MarkMonitor's revoke action (PATCH
    /// /certs/v1/order/{id}/revoke) has no field for a revocation reason code - its request schema
    /// only accepts cert/ignoreOrgCheck/additionalEmails - so the <paramref name="reason"/> parameter
    /// cannot be sent to MarkMonitor. It's accepted (rather than removed) to match
    /// IAnyCAPlugin.Revoke's signature; a non-default value is logged so it's visible that the
    /// reason was received but couldn't be forwarded, rather than silently dropped.
    /// </summary>
    public async Task<bool> RevokeCertificateAsync(string orderId, string orgName = null, uint reason = 0)
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Revoking certificate associated with order {OrderId}", orderId);
            if (reason != 0)
                _logger.LogWarning(
                    "Revocation reason {Reason} was requested for order {OrderId}, but MarkMonitor's revoke API has no field for a reason code - it will not be sent",
                    reason, orderId);
            await EnsureAuthenticatedAsync();

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

    private async Task EnsureAuthenticatedAsync()
    {
        if (string.IsNullOrEmpty(_bearerToken) || _tokenExpiresAtUtc == null ||
            _timeProvider.GetUtcNow().UtcDateTime >= _tokenExpiresAtUtc)
        {
            _logger.LogDebug("No valid bearer token on hand - authenticating");
            await AuthenticateAsync();
        }
    }

    private static string BuildErrorString(string jsonString)
    {
        var json = JObject.Parse(jsonString);
        var errorMessages = new List<string>();

        // MarkMonitor 400 responses: {"validations":[{"field":"cert.csr","code":"...","message":"..."}]}
        if (json["validations"] is JArray validations)
            foreach (var validation in validations)
            {
                var field = validation["field"]?.ToString();
                var code = validation["code"]?.ToString();
                var message = validation["message"]?.ToString();
                errorMessages.Add($"{field}: {message} ({code})");
            }
        // MarkMonitor 500 responses: {"errors":[{"code":"...","message":"..."}]}
        else if (json["errors"] is JArray errors)
            foreach (var error in errors)
            {
                var code = error["code"]?.ToString();
                var message = error["message"]?.ToString();
                errorMessages.Add($"{message} ({code})");
            }
        else if (json["validation_messages"] != null)
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

        return errorMessages.Any()
            ? string.Join(Environment.NewLine, errorMessages)
            : $"No recognized error format found in response: {jsonString}";
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