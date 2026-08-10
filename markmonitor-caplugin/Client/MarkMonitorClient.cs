using System.Collections.Concurrent;
using System.Diagnostics;
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
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Asn1.X9;
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
    private readonly SemaphoreSlim _authLock = new(1, 1);

    // Guards against creating a duplicate MarkMonitor order when Command retries an Enroll call -
    // whether the first attempt is still in flight (the retry races it) or already finished but its
    // response was lost (timeout, dropped connection, etc). Keyed on org+product+subject+CSR - an
    // identical CSR for the same subject/org/product within the window is treated as a retry, not a
    // distinct request. The value is reserved (via a TaskCompletionSource) *before* the real
    // CreateCertificateOrder call starts, so a retry that arrives while the first call is still
    // in-flight awaits the same in-progress result instead of starting a second order. Process-local
    // and short-lived by design: it's a guard against the specific narrow retry window, not a durable
    // dedup store (that would need to live in MarkMonitor itself).
    private static readonly TimeSpan RecentEnrollmentWindow = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, (DateTime ExpiresAtUtc, TaskCompletionSource<EnrollmentResult> Tcs)>
        _recentEnrollments = new();

    /// <summary>Test-only visibility into how many reservations (successful, failed, or in-flight)
    /// are currently held in <see cref="_recentEnrollments"/> - used to assert that
    /// <see cref="PruneExpiredReservations"/> actually bounds the dictionary's growth over time
    /// rather than accumulating one permanent entry per successful enrollment.</summary>
    internal int RecentEnrollmentsCount => _recentEnrollments.Count;

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
        // Every request this client makes wants an "application/json" Accept header, and it never
        // changes for the client's lifetime - set it once here rather than in FetchOrderAsync, where
        // an Add() on every call (Accept is a collection, not a single-value property) with no
        // preceding Remove() appended a fresh duplicate entry per call, unboundedly growing the
        // header list on this cached, long-lived HttpClient.
        _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
        _authLock.Dispose();
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
        _httpClient.DefaultRequestHeaders.Remove("X-API-KEY");
        _httpClient.DefaultRequestHeaders.Add("X-API-KEY", _apiKey);
        var requestBody = new TokenRequest
        {
            Username = _username,
            Password = _password
        };

        var requestUrl = $"{BaseUrl}/auth/v1/auth/authenticate";
        // Username (unlike ApiKey/Password) is not a secret - the plugin's own config schema marks
        // it Hidden=false - and is the only field that identifies which MarkMonitor service account
        // performed a given authentication. Log it so an auditor can reconstruct "who authenticated"
        // from this component's own logs, including when multiple CA connector instances (each with a
        // different service account) share one log sink.
        _logger.LogInformation("Authenticating with MarkMonitor API at {RequestUrl} as {Username}", requestUrl,
            _username);

        _logger.LogDebug("Sending authentication request");
        HttpResponseMessage response;
        try
        {
            response = await SendAndLogAsync(() => _httpClient.PostAsync(requestUrl,
                new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json")),
                "POST", requestUrl);
        }
        catch (Exception e)
        {
            // A transport-level failure (unreachable host, timeout, TLS failure) throws before
            // SendAndLogAsync's send() ever returns a response, so the IsSuccessStatusCode branch
            // below - the only other place this method logs "Authentication failed" - never runs.
            // Log it here too so this case still produces a self-contained, identity-tagged failure
            // record instead of only a generic, identity-less log line from whichever caller's own
            // catch block happens to receive the rethrown exception.
            _logger.LogError("Authentication failed for {Username}: {EMessage}", _username, e.Message);
            throw;
        }

        _logger.LogDebug("Reading authentication response");
        var content = await response.Content.ReadAsStringAsync();

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
            _logger.LogError("Authentication failed for {Username}: {EMessage}", _username, errMsg);
            throw new Exception(errMsg);
        }

        _logger.LogInformation("Authentication successful for {Username}", _username);
    }

    public async Task<int> GetCertificateInventoryAsync(string caId, string sort, int limit,
        BlockingCollection<AnyCAPluginCertificate> certificatesBuffer, CancellationToken cancelToken)
    {
        _logger.MethodEntry();
        var numberOfCertificates = 0;
        try
        {
            await EnsureAuthenticatedAsync();
            _logger.LogInformation("Retrieving certificate inventory from MarkMonitor");
            // Stream each page straight into certificatesBuffer as it arrives, via onPageReceived,
            // instead of letting ListCertificateOrdersAsync accumulate every page for the whole order
            // history into one in-memory list before this method ever touches the buffer - for a
            // large/long-lived org that meant unbounded peak memory and zero buffer throughput until
            // the entire (possibly huge) order history had downloaded.
            await ListCertificateOrdersAsync(0, caId, sort, limit, cancelToken, page =>
            {
                _logger.LogDebug("Retrieved a page of '{CertificateCount}' certificate orders", page.Count);
                foreach (var certificateDetail in page)
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
            });

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
        if (!string.IsNullOrEmpty(sort)) query.Add($"sort={Uri.EscapeDataString(sort)}");
        if (providerId > 0) query.Add($"providerId={providerId}");
        if (!string.IsNullOrEmpty(orgId)) query.Add($"organizationId={Uri.EscapeDataString(orgId)}");
        _logger.MethodExit();
        return query;
    }

    private List<string> BuildListOrgsQueryString(string name = "", string sort = "", int limit = 0, int page = 0)
    {
        _logger.MethodEntry();
        var query = new List<string>();
        if (page > 0) query.Add($"page={page}");
        if (limit > 0) query.Add($"size={limit}");
        if (!string.IsNullOrEmpty(sort)) query.Add($"sort={Uri.EscapeDataString(sort)}");
        if (!string.IsNullOrEmpty(name)) query.Add($"name={Uri.EscapeDataString(name)}");

        _logger.LogTrace("Query string: {Query}", query);
        _logger.MethodExit();
        return query;
    }

    /// <summary>Fetches every page of matching certificate orders. When <paramref name="onPageReceived"/>
    /// is supplied, each page is handed to it as soon as it arrives and is not also accumulated into
    /// the returned list (which is then null) - lets a caller with a large result set (e.g. a full
    /// inventory sync) process/forward orders page-by-page instead of holding the entire result set
    /// in memory and seeing zero progress until every page has downloaded. Omit it to get the
    /// original all-pages-in-one-list behavior every other caller relies on.</summary>
    public async Task<List<OrderContent>> ListCertificateOrdersAsync(int providerId, string orgId, string sort,
        int limit, CancellationToken cancelToken = default, Action<List<OrderContent>> onPageReceived = null)
    {
        _logger.MethodEntry();
        await EnsureAuthenticatedAsync();
        var output = onPageReceived == null ? new List<OrderContent>() : null;
        try
        {
            _logger.LogInformation("Retrieving certificate orders from MarkMonitor");
            var currentPage = 0;
            var allPagesDownloaded = false;

            do
            {
                cancelToken.ThrowIfCancellationRequested();

                var nextUrl =
                    $"{BaseUrl}/certs/v1/order";
                _logger.LogTrace("Base URL: {BaseUrl}", nextUrl);

                _logger.LogDebug("Building query string");
                var query = BuildQueryString(providerId, orgId, sort, limit, currentPage);
                if (query.Count > 0) nextUrl += "?" + string.Join("&", query);

                _logger.LogTrace("Getting page \'{CurrentPage}\' of \'{Limit}\')", currentPage, limit);

                _logger.LogDebug("Getting certificate orders from MarkMonitor {NextUrl}", nextUrl);
                var response = await SendAndLogAsync(() => _httpClient.GetAsync(nextUrl, cancelToken), "GET", nextUrl);

                _logger.LogDebug("Reading response content");
                var content = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode) throw new Exception(BuildErrorString(content));

                _logger.LogDebug("Deserializing response content to MarkMonitorListOrdersResponse");
                var certificateListResponse = JsonConvert.DeserializeObject<MarkMonitorListOrdersResponse>(content);
                if (onPageReceived != null)
                    onPageReceived(certificateListResponse.Content);
                else
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
                var response = await SendAndLogAsync(() => _httpClient.GetAsync(nextUrl), "GET", nextUrl);

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
            ValidateGuidFormat(orgId, nameof(orgId), "MarkMonitor organization ID");
            await EnsureAuthenticatedAsync();
            var url = $"{BaseUrl}/certs/v1/organization/{orgId}";
            _logger.LogDebug("Getting organization from MarkMonitor {Url}", url);
            var response = await SendAndLogAsync(() => _httpClient.GetAsync(url), "GET", url);
            var content = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) throw new Exception(BuildErrorString(content));

            return JsonConvert.DeserializeObject<MarkMonitorOrganizationResponse>(content);
        }
        catch (Exception e)
        {
            // Rethrow rather than swallow to null - a transient auth/network/parsing failure here
            // must not be reported to the caller identically to "this organization doesn't exist"
            // (same class of gap already fixed in GetSingleOrderAsync).
            _logger.LogError("An error has occurred: {EMessage}", e.Message);
            throw;
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
                var response = await SendAndLogAsync(() => _httpClient.GetAsync(nextUrl), "GET", nextUrl);

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

    /// <summary>Fetches the raw order content for a single order ID (used both to build the
    /// public-facing AnyCAPluginCertificate and, internally, to verify an order's owning
    /// organization before revoking or cancelling it).</summary>
    private async Task<OrderContent> FetchOrderAsync(string orderId)
    {
        ValidateGuidFormat(orderId, nameof(orderId), "MarkMonitor order ID");
        await EnsureAuthenticatedAsync();

        // Do NOT re-set the Authorization header here: EnsureAuthenticatedAsync/AuthenticateAsync
        // already set it once, under _authLock, whenever the token is (re)established - every other
        // call site in this class relies on that same invariant. Setting it again here, unguarded,
        // let a concurrent FetchOrderAsync call (or a concurrent re-authentication) race writes to
        // the shared HttpClient's Authorization header (see GitHub issue #8). The Accept header is
        // likewise set once, in the constructor - not here on every call (see its comment there).
        var orderUrl = $"{BaseUrl}/certs/v1/order/{orderId}";
        var response = await SendAndLogAsync(() => _httpClient.GetAsync(orderUrl), "GET", orderUrl);
        var content = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode) throw new Exception(BuildErrorString(content));
        return JsonConvert.DeserializeObject<OrderContent>(content);
    }

    /// <summary>Resolves a CA connection's configured OrgId (which may be a friendly name or a
    /// GUID) to the organization's real GUID.</summary>
    private async Task<string> ResolveOrganizationIdAsync(string orgNameOrId)
    {
        if (Guid.TryParse(orgNameOrId, out _)) return orgNameOrId;
        // A page size of 100 (not 1) here: MarkMonitor's name filter can return several fuzzy
        // matches for one configured name, and a page size of 1 would force one sequential HTTP
        // round-trip per match just to page through them all before the exact-match filter below
        // ever runs - this fetches all of them in one request instead.
        var orgs = await ListOrganizationsAsync(0, 100, orgNameOrId);
        // MarkMonitor's own name filter may do substring/fuzzy matching rather than exact matching,
        // so filter to an exact (case-insensitive) name match ourselves rather than trusting the
        // first result - otherwise a configured name that's a substring of another org's name (e.g.
        // "Acme" vs "Acme Corp Europe") could silently resolve to the wrong organization, which would
        // undermine the cross-org ownership check in RevokeCertificateAsync.
        return orgs.FirstOrDefault(o => string.Equals(o.Name, orgNameOrId, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    /// <summary>
    /// Verifies that <paramref name="orderId"/> belongs to the configured organization before
    /// allowing a destructive action (revoke/cancel) against it. Shared by
    /// <see cref="RevokeCertificateAsync"/> and <see cref="CancelCertificateAsync"/> so the two stay
    /// behaviorally identical on this check.
    ///
    /// MarkMonitor's own ignoreOrgCheck=false default only guards against acting on an order that
    /// belongs to a different reseller account entirely - it has no notion of the specific
    /// sub-organization this CA connector is scoped to. That distinction matters most for
    /// RenewOrReissue, where the order ID being revoked comes from Command's ICertificateDataReader
    /// rather than from this org's own enrollment - so verify it here rather than trusting the
    /// caller (or MarkMonitor) to have scoped it correctly.
    ///
    /// A blank orgName intentionally skips this check (ad-hoc/manual callers that don't scope by
    /// organization), but that must never happen silently - it's logged explicitly so a production
    /// caller unexpectedly hitting this path (e.g. a misconfigured OrgId) is visible in the logs
    /// rather than looking identical to a passing check.
    /// </summary>
    /// <param name="orderId">The order being acted on.</param>
    /// <param name="orgName">The configured organization name or GUID; blank skips the check.</param>
    /// <param name="actionGerund">Present-participle form of the action for log text (e.g. "Revoking", "Cancelling").</param>
    /// <param name="actionVerb">Infinitive form of the action for log/exception text (e.g. "revoke", "cancel").</param>
    private async Task EnsureOrderBelongsToOrganizationAsync(string orderId, string orgName, string actionGerund,
        string actionVerb)
    {
        if (string.IsNullOrWhiteSpace(orgName))
        {
            _logger.LogWarning(
                "{ActionGerund} order {OrderId} with no organization to verify ownership against - the cross-organization ownership check was skipped",
                actionGerund, orderId);
            return;
        }

        var expectedOrgId = await ResolveOrganizationIdAsync(orgName);
        var order = await FetchOrderAsync(orderId);
        // Comparing as parsed Guids, not raw strings: Guid.TryParse accepts several textual
        // formats (braces, no dashes, etc.), so an admin-configured OrgId in a non-canonical
        // format must still match MarkMonitor's own canonical serialization of the same GUID.
        var actualOrgIdParsed = Guid.TryParse(order.OrganizationId, out var actualOrgId) ? actualOrgId : (Guid?)null;
        var expectedOrgIdParsed = expectedOrgId != null && Guid.TryParse(expectedOrgId, out var parsedExpected)
            ? parsedExpected
            : (Guid?)null;
        if (expectedOrgIdParsed == null || actualOrgIdParsed == null || actualOrgIdParsed != expectedOrgIdParsed)
        {
            _logger.LogError(
                "Refusing to {ActionVerb} order {OrderId}: it belongs to organization {ActualOrgId}, but the configured organization {ConfiguredOrgName} resolved to {ExpectedOrgId}",
                actionVerb, orderId, order.OrganizationId, orgName, expectedOrgId);
            throw new Exception($"Order {orderId} belongs to a different organization than the configured '{orgName}' - refusing to {actionVerb} it");
        }
    }

    public async Task<AnyCAPluginCertificate> GetSingleOrderAsync(string orderId)
    {
        try
        {
            var order = await FetchOrderAsync(orderId);

            // order.Cert can be null for an order that hasn't progressed far enough yet (e.g.
            // CREATED/DIGI_NEEDS_CSR) - same class of gap already fixed in GetCertificateInventoryAsync.
            DateTime? revocationDate = null;
            if (order.Cert?.RevokeStatus == "REVOKED") revocationDate = Convert.ToDateTime(order.Cert.DateValidUntil);

            return new AnyCAPluginCertificate
            {
                CARequestID = order.Id,
                Certificate = order.Cert?.EndEntityCert,
                Status = MarkMonitorCertificateStatusToCAStatus(order),
                ProductID = order.CertType,
                RevocationDate = revocationDate
            };
        }
        catch (Exception e)
        {
            // Rethrow rather than swallow to null - GetSingleRecord (the connector method that
            // calls this) needs the real exception to log a failure instead of silently reporting
            // "not found" for what was actually an auth/network/parsing error.
            _logger.LogError("An error has occurred: {EMessage}", e.Message);
            throw;
        }
    }

    /// <summary>
    /// Order IDs are interpolated directly into request URLs - a corrupted or manipulated
    /// CARequestID should fail fast with a clear error rather than silently producing an unexpected
    /// path segment.
    /// </summary>
    private static void ValidateGuidFormat(string value, string paramName, string description)
    {
        if (!Guid.TryParse(value, out _))
            // The rejected value is embedded in this exception's own Message, which multiple callers
            // (this class's own catch blocks, and MarkMonitorCAConnector's) log verbatim via e.Message
            // - sanitize it here, at the source, rather than trying to catch every downstream log call
            // that might surface it (CWE-117).
            throw new ArgumentException(
                $"'{LogSanitizer.ForLog(value)}' is not a valid {description} (expected a GUID)", paramName);
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

    private const string EcPublicKeyOid = "1.2.840.10045.2.1";

    /// <summary>
    /// MarkMonitor's DigiCert-backed products silently reject an ECC CSR whose public key uses
    /// explicit curve parameters (the curve's prime/coefficients/base point spelled out) instead of
    /// a named-curve OID reference - the order fails almost instantly with no reason surfaced
    /// anywhere in MarkMonitor's API (confirmed by decoding a real rejected order's CSR). CA/Browser
    /// Forum baseline requirements disallow explicit parameters for publicly-trusted certs, so this
    /// fails fast with an actionable message here rather than silently forwarding an order that
    /// MarkMonitor will just as silently fail.
    /// </summary>
    private static void ValidateEccCsrUsesNamedCurve(SubjectPublicKeyInfo publicKeyInfo)
    {
        if (publicKeyInfo.Algorithm.Algorithm.Id != EcPublicKeyOid) return;

        var ecParameters = X962Parameters.GetInstance(publicKeyInfo.Algorithm.Parameters);
        if (!ecParameters.IsNamedCurve)
            throw new ArgumentException(
                "ECC CSR uses explicit curve parameters instead of a named curve (e.g. P-256/secp256r1) - MarkMonitor requires a named curve and will silently fail the order otherwise");
    }

    // A reservation is "still active" - and must keep being awaited rather than replaced - if either
    // its nominal window hasn't elapsed yet, or its own call simply hasn't finished yet. The latter
    // matters when a call's real work (org/group lookups, CSR parsing, the order-create HTTP call
    // itself) takes longer than RecentEnrollmentWindow: without it, a retry arriving after the nominal
    // window - but while the original call is still genuinely in flight - would win a fresh
    // reservation and create a real second order, exactly the outcome this cache exists to prevent.
    private static bool IsReservationStillActive(
        (DateTime ExpiresAtUtc, TaskCompletionSource<EnrollmentResult> Tcs) entry, DateTime now) =>
        entry.ExpiresAtUtc > now || !entry.Tcs.Task.IsCompleted;

    // _recentEnrollments has no eviction path for a *successful* enrollment - the dedupe key is
    // built from the CSR, which is unique per real-world request, so a completed success entry is
    // essentially never looked up again and would otherwise sit in the dictionary (holding the full
    // CSR and issued cert chain) for the remaining lifetime of the process. Sweeping expired-and-
    // completed entries here, on every enrollment call, keeps the dictionary bounded by "enrollments
    // within the last RecentEnrollmentWindow" instead of "enrollments ever performed" - without a
    // separate timer/thread to manage. Only entries IsReservationStillActive already says are safe to
    // drop (window elapsed AND the call finished) are removed, so a genuinely in-flight reservation is
    // never touched.
    private void PruneExpiredReservations(DateTime now)
    {
        foreach (var entry in _recentEnrollments)
            if (!IsReservationStillActive(entry.Value, now))
                TryRemoveReservation(entry.Key, entry.Value);
    }

    // ConcurrentDictionary's own Remove(key) doesn't check the value, so it can delete a different
    // caller's reservation that has since replaced the one this call actually owned for the same
    // key - going through ICollection<KeyValuePair<>> gives an atomic, conditional remove-if-still-
    // equal-to-this-value instead.
    private bool TryRemoveReservation(string key, (DateTime ExpiresAtUtc, TaskCompletionSource<EnrollmentResult> Tcs) entry) =>
        ((ICollection<KeyValuePair<string, (DateTime, TaskCompletionSource<EnrollmentResult>)>>)_recentEnrollments)
            .Remove(new KeyValuePair<string, (DateTime, TaskCompletionSource<EnrollmentResult>)>(key, entry));

    public async Task<EnrollmentResult> EnrollCertificateAsync(string csr, string subject,
        Dictionary<string, string[]> san, string orderType, Dictionary<string, string> productParams,
        MarkMonitorConfig config)
    {
        _logger.MethodEntry();
        var dedupeKey = $"{config.OrgName}|{orderType}|{subject}|{csr}";
        // Subject is fully requester-controlled (straight off the submitted CSR) - log a CR/LF-
        // escaped copy everywhere below so an embedded CR/LF can't forge a fake log line (CWE-117).
        var logSafeSubject = LogSanitizer.ForLog(subject);
        TaskCompletionSource<EnrollmentResult> ownedReservation = null;
        (DateTime ExpiresAtUtc, TaskCompletionSource<EnrollmentResult> Tcs) ownedEntry = default;
        // Only a failure at or after the actual order-create call can mean "MarkMonitor might have
        // created the order before we found out" - a network blip during an earlier step (org/contact/
        // group resolution, CSR parsing) never reached that endpoint at all, so it can never have
        // created an order and must not lock out a same-second retry for the rest of the window.
        var reachedCreateOrderCall = false;
        try
        {
            await EnsureAuthenticatedAsync();

            var now = _timeProvider.GetUtcNow().UtcDateTime;
            PruneExpiredReservations(now);
            if (_recentEnrollments.TryGetValue(dedupeKey, out var existing) && IsReservationStillActive(existing, now))
            {
                var dedupedResult = await existing.Tcs.Task;
                _logger.LogWarning(
                    "An identical enrollment for subject {Subject} was already submitted in the last {Minutes} minute(s) - folded into existing order {CARequestID} instead of creating a duplicate",
                    logSafeSubject, RecentEnrollmentWindow.TotalMinutes, dedupedResult.CARequestID);
                return dedupedResult;
            }

            // Reserve this key *before* doing any real work, so a retry that arrives while this
            // call is still in flight (the scenario this cache actually exists to prevent) finds
            // the reservation and awaits it, rather than racing to create a second order.
            var candidateTcs =
                new TaskCompletionSource<EnrollmentResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            // AddOrUpdate, not GetOrAdd: an expired-and-completed entry must be replaced, not returned
            // as-is - GetOrAdd would hand back the stale (already-completed) reservation forever once
            // one exists for this key. A reservation is only replaced once BOTH its nominal window has
            // elapsed AND its own call has actually finished - a still-running call that happens to run
            // longer than the window must keep being awaited, not raced by a second real order.
            var reserved = _recentEnrollments.AddOrUpdate(
                dedupeKey,
                (now + RecentEnrollmentWindow, candidateTcs),
                (_, current) => IsReservationStillActive(current, now)
                    ? current
                    : (now + RecentEnrollmentWindow, candidateTcs));
            if (reserved.Tcs != candidateTcs)
            {
                _logger.LogWarning(
                    "An identical enrollment for subject {Subject} is already in flight - awaiting that result instead of creating a duplicate order",
                    logSafeSubject);
                return await reserved.Tcs.Task;
            }

            ownedReservation = candidateTcs;
            ownedEntry = reserved;

            var caseInsensitiveParams = new Dictionary<string, string>(productParams, StringComparer.OrdinalIgnoreCase);

            var additionalEmails =
                caseInsensitiveParams.GetValueOrDefault("additionalEmails");
            var additionalEmailsList = new List<string>();
            if (!string.IsNullOrEmpty(additionalEmails))
            {
                additionalEmails = additionalEmails.Replace(" ", ",");
                additionalEmailsList = additionalEmails
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            var org = await ResolveOrganizationAsync(config.OrgName);
            var orgIdGuid = Guid.Parse(org.Id);

            var comments = caseInsensitiveParams.GetValueOrDefault(
                MarkMonitorCAPluginConfig.EnrollmentConfigConstants.Comments, "Requested via Keyfactor Command");
            _logger.LogTrace("Comments: {Comments}", LogSanitizer.ForLog(comments));
            var locale = caseInsensitiveParams.GetValueOrDefault(
                MarkMonitorCAPluginConfig.EnrollmentConfigConstants.Locale, "en");
            _logger.LogTrace("Locale: {Locale}", LogSanitizer.ForLog(locale));
            var provider = caseInsensitiveParams.GetValueOrDefault(
                MarkMonitorCAPluginConfig.EnrollmentConfigConstants.Provider, "DIGICERT");
            _logger.LogTrace("Provider: {Provider}", LogSanitizer.ForLog(provider));

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
            var groupIdGuid = await ResolveGroupIdAsync(groupParam);

            var dcvMethod = ValidateDcvMethod(caseInsensitiveParams.GetValueOrDefault(
                MarkMonitorCAPluginConfig.EnrollmentConfigConstants.DCVMethod,
                DomainControlValidationMethods.Email.GetDescription()));

            // Lookup order type in CertOrderTypes enum
            _logger.LogDebug("Looking up order type {OrderType} in CertOrderTypes enum", orderType);
            var certOrderType = Enum.Parse<CertOrderTypes>(orderType);

            _logger.LogDebug("Deserializing CSR");
            var csrObject = new Pkcs10CertificationRequest(GetCsrBytes(csr));
            var csrInfo = csrObject.GetCertificationRequestInfo();
            ValidateEccCsrUsesNamedCurve(csrInfo.SubjectPublicKeyInfo);

            _logger.LogDebug("Determining CSR algorithm");
            var requestAlgorithm = getCsrAlgorithm(csrObject);
            _logger.LogTrace("CSR algorithm: {RequestAlgorithm}", requestAlgorithm);

            _logger.LogDebug("Converting CSR to PEM");
            var csrPem = PemUtilities.DERToPEM(csrObject.GetEncoded(), PemUtilities.PemObjectType.CertRequest);

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
                    // dcvEmails is not marked required by MarkMonitor's schema, and leaving it empty
                    // has been verified against the live API to succeed for DCVMethod=EMAIL -
                    // MarkMonitor falls back to the domain/org's registered DCV contacts. Revisit if
                    // that ever changes; there's no documented case where an explicit approver email
                    // is actually required here.
                    DcvEmails = new List<DcvEmail>(),
                    AlgorithmHash = requestAlgorithm
                }
            };


            _logger.LogDebug("Calling CreateCertificateOrder");
            logCreateOrderRequest(certOrder);
            reachedCreateOrderCall = true;
            var order = await CreateCertificateOrder(certOrder);

            if (order == null) throw new Exception($"Failed to enroll certificate `{logSafeSubject}` with MarkMonitor");

            _logger.LogInformation("Certificate enrolled successfully");
            _logger.LogInformation(
                "Order {CARequestID} was created with ContactId {ContactId} and GroupId {GroupId}", order.Id,
                resolvedContact?.Id, groupIdGuid);
            var enrollmentResult = new EnrollmentResult
            {
                CARequestID = order.Id,
                Certificate = order.Cert?.EndEntityCert,
                Status = MarkMonitorCertificateStatusToCAStatus(order),
                StatusMessage = "MarkMonitor order status: " + order.Status
            };
            ownedReservation.SetResult(enrollmentResult);
            return enrollmentResult;
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred: {EMessage}", e.Message);
            if (ownedReservation != null)
            {
                ownedReservation.SetException(e);

                // A transport-level failure (timeout, dropped connection) DURING the order-create call
                // itself means we genuinely don't know whether MarkMonitor actually created the order
                // before the failure - exactly the ambiguous case this cache exists to guard against
                // (see the class-level comment). Don't evict the reservation for that case: keep it in
                // _recentEnrollments so a Command retry within the window is folded into this (now-
                // faulted) reservation instead of racing ahead to create a second real order. The same
                // exception types raised by an EARLIER step (org/contact/group resolution, CSR parsing)
                // are NOT ambiguous - reachedCreateOrderCall gates on that, since those steps never
                // reach MarkMonitor's create-order endpoint and so can never have created an order; a
                // retry after one of those must get a fresh attempt immediately, not be locked out for
                // the rest of the window by an unrelated transient blip. Conditional Remove, not a bare
                // key-based one: if this reservation's own window already expired while this call was
                // still running, a different caller may have since won a fresh reservation for the same
                // key - an unconditional remove would delete THEIR entry instead of (the no-longer-
                // present) one this call owned.
                // MarkMonitorOrderCreatedButUnparsableException means MarkMonitor already confirmed
                // (2xx) the order was created - stronger than merely ambiguous - so it must never be
                // treated as safe to evict either.
                var isAmbiguousOutcome = reachedCreateOrderCall &&
                    e is HttpRequestException or TaskCanceledException or MarkMonitorOrderCreatedButUnparsableException;
                if (isAmbiguousOutcome)
                    _logger.LogWarning(
                        "Enrollment for subject {Subject} failed with an ambiguous network-level error - keeping the retry-dedup reservation active for the rest of the window so a retry doesn't risk creating a duplicate MarkMonitor order",
                        logSafeSubject);
                else
                    TryRemoveReservation(dedupeKey, ownedEntry);
            }

            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    private async Task<MarkMonitorOrganizationResponse> ResolveOrganizationAsync(string orgNameOrId)
    {
        MarkMonitorOrganizationResponse org;
        if (Guid.TryParse(orgNameOrId, out _))
        {
            _logger.LogDebug("OrgId '{OrgName}' looks like a GUID - fetching the organization directly",
                orgNameOrId);
            org = await GetOrganizationAsync(orgNameOrId);
        }
        else
        {
            // Page size 100, not 1 - see ResolveOrganizationIdAsync's comment on the same call.
            var orgs = await ListOrganizationsAsync(0, 100, orgNameOrId);
            _logger.LogTrace("Organizations found: {@Orgs}", orgs);
            // ListOrganizationsAsync throws rather than returning null on error, so orgs is never
            // null here - just possibly empty. MarkMonitor's own name filter may do substring/fuzzy
            // matching rather than exact matching, so filter to an exact (case-insensitive) name
            // match ourselves rather than trusting the first result - otherwise a configured name
            // that's a substring of another org's name (e.g. "Acme" vs "Acme Corp Europe") could
            // silently resolve to the wrong organization.
            org = orgs.FirstOrDefault(o => string.Equals(o.Name, orgNameOrId, StringComparison.OrdinalIgnoreCase));
        }

        _logger.LogTrace("Organization ID: {OrgId}", org?.Id);
        if (string.IsNullOrEmpty(org?.Id))
        {
            _logger.LogError("Organization ID not found for {OrgName}", orgNameOrId);
            throw new InvalidDataException($"Organization ID '{orgNameOrId}' not found");
        }

        return org;
    }

    // Unlike contacts (scoped to an organization's own Contacts list), group resolution can't be
    // scoped to the configured organization: MarkMonitor's Auth API models groups as
    // account/tenant-wide - /auth/v1/group has no organizationId filter, and the Group schema it
    // returns (id/name/description/dateCreated/dateUpdated) has no organizationId field to check
    // against either. There is nothing in this API to scope against, so a group name/GUID that
    // resolves at all is accepted as-is; matching is by exact (case-insensitive) name rather than a
    // substring, which is the closest available mitigation.
    private async Task<Guid?> ResolveGroupIdAsync(string groupParam)
    {
        if (string.IsNullOrWhiteSpace(groupParam)) return null;

        if (Guid.TryParse(groupParam, out var parsedGroupId)) return parsedGroupId;

        // Page size 100, not 0 (server default) - see ResolveOrganizationIdAsync's comment on the
        // identical fuzzy-match, filter-by-exact-name pattern: a small page size costs one sequential
        // HTTP round-trip per fuzzy match instead of one round-trip total.
        var groups = await ListGroupsAsync(0, 100, groupParam);
        var matchedGroup = groups?.FirstOrDefault(g =>
            string.Equals(g.Name, groupParam, StringComparison.OrdinalIgnoreCase));
        if (matchedGroup != null) return Guid.Parse(matchedGroup.Id);

        _logger.LogWarning("MarkMonitor group '{GroupParam}' could not be resolved to an ID",
            LogSanitizer.ForLog(groupParam));
        return null;
    }

    private string ValidateDcvMethod(string dcvMethod)
    {
        var validDcvMethods = Enum.GetValues<DomainControlValidationMethods>()
            .Select(m => m.GetDescription()).ToList();
        if (validDcvMethods.Contains(dcvMethod, StringComparer.OrdinalIgnoreCase)) return dcvMethod;

        _logger.LogWarning("Invalid DCVMethod '{DcvMethod}' specified, defaulting to EMAIL",
            LogSanitizer.ForLog(dcvMethod));
        return DomainControlValidationMethods.Email.GetDescription();
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
                LogSanitizer.ForLog(contactParam));
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
            if (string.IsNullOrWhiteSpace(subject)) return subject;

            try
            {
                var cnValues = new X509Name(subject).GetValueList(X509Name.CN);
                if (cnValues.Count > 0) return (string)cnValues[cnValues.Count - 1];
            }
            catch (Exception e)
            {
                _logger.LogDebug(
                    "Could not parse subject '{Subject}' as an X509 DN, falling back to string search: {EMessage}",
                    LogSanitizer.ForLog(subject), e.Message);
            }

            // Fallback for a subject that isn't a fully valid DN (e.g. bare "CN=foo" with no other RDNs).
            var cnPrefix = "CN=";
            var cnIndex = subject.IndexOf(cnPrefix, StringComparison.OrdinalIgnoreCase);
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
        _logger.LogTrace("CommonName: {CommonName}", LogSanitizer.ForLog(request.Cert.CommonName));
        _logger.LogTrace("OrganizationId: {OrganizationId}", request.OrganizationId);
        _logger.LogTrace("GroupId: {GroupId}", request.GroupId);
        _logger.LogTrace("CertType: {CertType}", request.CertType);
        _logger.LogTrace("Locale: {Locale}", LogSanitizer.ForLog(request.Locale));
        _logger.LogTrace("Provider: {Provider}", LogSanitizer.ForLog(request.Provider));
        _logger.LogTrace("Comments: {Comments}", LogSanitizer.ForLog(request.Comments));
        // Deliberately not logging AdditionalEmails (requester PII) or the CSR/full Cert object -
        // just enough to confirm the shape of the request without leaking their content.
        _logger.LogTrace("AdditionalEmails count: {AdditionalEmailsCount}", request.AdditionalEmails?.Count ?? 0);
        _logger.LogTrace("SkipPrice: {SkipPrice}", request.SkipPrice);
        _logger.LogTrace("DcvMethod: {DcvMethod}", LogSanitizer.ForLog(request.Cert.DcvMethod));
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
            var response = await SendAndLogAsync(() => _httpClient.PostAsync(url, jsonPayload), "POST", url);
            _logger.LogTrace("Response: {Response}", response);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            _logger.LogTrace("Response content: {Content}", content);
            if (response.IsSuccessStatusCode)
            {
                OrderContent order;
                try
                {
                    order = JsonConvert.DeserializeObject<OrderContent>(content);
                }
                catch (Exception parseException)
                {
                    // MarkMonitor already returned a success status here - the order was definitely
                    // created, even though its response body didn't parse - so this must be treated
                    // as at least as ambiguous as a network-level failure (EnrollCertificateAsync's
                    // isAmbiguousOutcome check matches on this type), not as a definite non-creation
                    // that's safe to let a retry create a genuine duplicate order for.
                    throw new MarkMonitorOrderCreatedButUnparsableException(
                        "MarkMonitor returned a successful response for order creation, but its body could not be parsed",
                        parseException);
                }

                if (order == null)
                    // An empty body or a literal "null" both deserialize to null without throwing -
                    // same "MarkMonitor confirmed creation, but we can't read the result" situation as
                    // the catch above, just without an exception to wrap. Must not fall through to
                    // order.Id below, which would throw a plain NullReferenceException that
                    // EnrollCertificateAsync's isAmbiguousOutcome check doesn't recognize as ambiguous.
                    throw new MarkMonitorOrderCreatedButUnparsableException(
                        "MarkMonitor returned a successful response for order creation, but its body was empty or null",
                        null);

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

    public async Task<bool> CancelCertificateAsync(string orderId, string orgName = null)
    {
        _logger.MethodEntry();
        try
        {
            ValidateGuidFormat(orderId, nameof(orderId), "MarkMonitor order ID");
            // orderId is caller-supplied - log a CR/LF-escaped copy so an embedded CR/LF can't forge a
            // fake log line (CWE-117). ValidateGuidFormat above already guarantees it's GUID-shaped
            // for normal (non-exceptional) calls, but its own exception message is sanitized too, so
            // this covers the exceptional path as well.
            var logSafeOrderId = LogSanitizer.ForLog(orderId);
            _logger.LogInformation("Cancelling certificate {CertificateId}", logSafeOrderId);
            await EnsureAuthenticatedAsync();

            await EnsureOrderBelongsToOrganizationAsync(orderId, orgName, "Cancelling", "cancel");

            var url = $"{BaseUrl}/certs/v1/order/{orderId}/cancel";
            _logger.LogDebug("Cancelling certificate at {Url}", url);
            var payload = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await SendAndLogAsync(() => _httpClient.PatchAsync(url, payload), "PATCH", url);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Certificate {CertificateId} has been cancelled", logSafeOrderId);
                return true;
            }

            var errMsg = BuildErrorString(content);
            _logger.LogError("An error has occurred while attempting to cancel order {CertificateId}: {EMessage}",
                logSafeOrderId, errMsg);
            throw new Exception(errMsg);
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred while attempting to cancel {CertificateId}: {EMessage}",
                LogSanitizer.ForLog(orderId), e.Message);
            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    public async Task<bool> ReissueCertificateAsync(string orderId, MarkMonitorReissueRequest payload)
    {
        _logger.MethodEntry();
        try
        {
            ValidateGuidFormat(orderId, nameof(orderId), "MarkMonitor order ID");
            // orderId is caller-supplied - log a CR/LF-escaped copy so an embedded CR/LF can't forge a
            // fake log line (CWE-117).
            var logSafeOrderId = LogSanitizer.ForLog(orderId);
            _logger.LogInformation("Revoking certificate {CertificateId}", logSafeOrderId);
            await EnsureAuthenticatedAsync();

            var url = $"{BaseUrl}/certs/v1/order/{orderId}/reissue";
            _logger.LogDebug("Reissuing certificate at {Url}", url);
            //convert payload to json
            var jsonPayload = new StringContent(
                JsonConvert.SerializeObject(payload),
                Encoding.UTF8, "application/json"
            );
            _logger.LogTrace("Reissue payload: {@Payload}", jsonPayload);
            var response = await SendAndLogAsync(() => _httpClient.PatchAsync(url, jsonPayload), "PATCH", url);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Certificate {CertificateId} has been reissued", logSafeOrderId);
                return true;
            }

            var errMsg = BuildErrorString(content);
            _logger.LogError("An error has occurred while attempting to reissue {CertificateId}: {EMessage}",
                logSafeOrderId, errMsg);
            throw new Exception(errMsg);
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred while attempting to reissue {CertificateId}: {EMessage}",
                LogSanitizer.ForLog(orderId), e.Message);
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
            ValidateGuidFormat(orderId, nameof(orderId), "MarkMonitor order ID");
            // orderId is caller-supplied - log a CR/LF-escaped copy so an embedded CR/LF can't forge a
            // fake log line (CWE-117).
            var logSafeOrderId = LogSanitizer.ForLog(orderId);
            _logger.LogInformation("Revoking certificate associated with order {OrderId}", logSafeOrderId);
            if (reason != 0)
                _logger.LogWarning(
                    "Revocation reason {Reason} was requested for order {OrderId}, but MarkMonitor's revoke API has no field for a reason code - it will not be sent",
                    reason, logSafeOrderId);
            await EnsureAuthenticatedAsync();

            await EnsureOrderBelongsToOrganizationAsync(orderId, orgName, "Revoking", "revoke");

            var url = $"{BaseUrl}/certs/v1/order/{orderId}/revoke";
            _logger.LogDebug("Revoking certificate at {Url}", url);
            var payload = new StringContent("{}", Encoding.UTF8, "application/json");
            var response = await SendAndLogAsync(() => _httpClient.PatchAsync(url, payload), "PATCH", url);

            _logger.LogDebug("Reading response content");
            var content = await response.Content.ReadAsStringAsync();
            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Certificate {CertificateId} has been revoked", logSafeOrderId);
                return true;
            }

            var errMsg = BuildErrorString(content);
            _logger.LogError("An error has occurred while attempting to revoke {CertificateId}: {EMessage}",
                logSafeOrderId, errMsg);
            throw new Exception(errMsg);
        }
        catch (Exception e)
        {
            _logger.LogError("An error has occurred while attempting to revoke {CertificateId}: {EMessage}",
                LogSanitizer.ForLog(orderId), e.Message);
            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    private bool TokenNeedsRefresh() =>
        string.IsNullOrEmpty(_bearerToken) || _tokenExpiresAtUtc == null ||
        _timeProvider.GetUtcNow().UtcDateTime >= _tokenExpiresAtUtc;

    private async Task EnsureAuthenticatedAsync()
    {
        // Double-checked locking: without the lock, two callers can both see an expired token,
        // both call AuthenticateAsync concurrently, and race writing _bearerToken/_tokenExpiresAtUtc
        // and the shared HttpClient's Authorization header - one of them can end up sending requests
        // under the other's (or a half-written) token.
        if (!TokenNeedsRefresh()) return;

        await _authLock.WaitAsync();
        try
        {
            if (TokenNeedsRefresh())
            {
                _logger.LogDebug("No valid bearer token on hand - authenticating");
                await AuthenticateAsync();
            }
            else
            {
                _logger.LogDebug("Bearer token was refreshed by a concurrent caller while waiting on the auth lock - reusing it");
            }
        }
        finally
        {
            _authLock.Release();
        }
    }

    /// <summary>Runs an HTTP call and logs its method/URL/status code/elapsed time - none of this
    /// class's call sites otherwise captured that response metadata (only AuthenticateAsync logged a
    /// status code, and only at Trace), leaving no basis in this component's own logs for latency- or
    /// status-code-based anomaly detection or vendor API call forensic reconstruction.</summary>
    private async Task<HttpResponseMessage> SendAndLogAsync(Func<Task<HttpResponseMessage>> send, string method,
        string url)
    {
        var stopwatch = Stopwatch.StartNew();
        var response = await send();
        stopwatch.Stop();
        _logger.LogInformation("{Method} {Url} -> {StatusCode} ({ElapsedMs}ms)", method, url,
            (int)response.StatusCode, stopwatch.ElapsedMilliseconds);
        return response;
    }

    private static string BuildErrorString(string jsonString)
    {
        var json = JObject.Parse(jsonString);
        var errorMessages = new List<string>();

        // Every value pulled out of MarkMonitor's response below is sanitized before it's woven into
        // errorMessages - a validation API quoting back the offending value is a common pattern, and
        // this component's own request could easily be why: e.g. a CSR-derived CommonName/SAN entry
        // with an embedded CR/LF (CSR ASN.1 encoding doesn't prevent that). Without this, that CR/LF
        // would forge a log entry (CWE-117) via BuildErrorString's result, despite the sanitization
        // already applied everywhere else a requester-influenceable string reaches this component's
        // logs. Sanitizing each value here - not the joined result - preserves the intentional
        // Environment.NewLine separators between distinct errors below.
        // MarkMonitor 400 responses: {"validations":[{"field":"cert.csr","code":"...","message":"..."}]}
        if (json["validations"] is JArray validations)
            foreach (var validation in validations)
            {
                var field = LogSanitizer.ForLog(validation["field"]?.ToString());
                var code = LogSanitizer.ForLog(validation["code"]?.ToString());
                var message = LogSanitizer.ForLog(validation["message"]?.ToString());
                errorMessages.Add($"{field}: {message} ({code})");
            }
        // MarkMonitor 500 responses: {"errors":[{"code":"...","message":"..."}]}
        else if (json["errors"] is JArray errors)
            foreach (var error in errors)
            {
                var code = LogSanitizer.ForLog(error["code"]?.ToString());
                var message = LogSanitizer.ForLog(error["message"]?.ToString());
                errorMessages.Add($"{message} ({code})");
            }
        else if (json["validation_messages"] != null)
            foreach (var validationMessage in json["validation_messages"])
            {
                var field = LogSanitizer.ForLog(validationMessage.Path);
                var fieldErrors = (JObject)validationMessage.First;

                foreach (var error in fieldErrors)
                {
                    var errorMessage = LogSanitizer.ForLog(error.Value.ToString());
                    errorMessages.Add($"{field}: {errorMessage}");

                    if (error.Key == "options")
                    {
                        var options = string.Join(", ",
                            error.Value.ToObject<List<string>>().Select(LogSanitizer.ForLog));
                        errorMessages.Add($"{field} options: {options}");
                    }
                }
            }
        else if (json["detail"] != null)
            errorMessages.Add(LogSanitizer.ForLog(json["detail"].ToString()));

        if (errorMessages.Any()) return string.Join(Environment.NewLine, errorMessages);

        // Unrecognized shape - order/contact payloads can carry customer PII (name, email), so
        // truncate rather than dumping the full response body verbatim into an error-level log.
        const int maxLength = 200;
        var truncated = jsonString.Length > maxLength ? jsonString[..maxLength] + "... (truncated)" : jsonString;
        return $"No recognized error format found in response: {LogSanitizer.ForLog(truncated)}";
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
            // EndEntityStatus.INITIALIZED is not what the AnyGatewayREST framework treats as
            // "accepted, still pending" - that's EXTERNALVALIDATION. Returning INITIALIZED here
            // caused the gateway to report a hard enrollment failure for an order that had, in
            // fact, been created successfully at MarkMonitor and was simply awaiting DCV/issuance
            // (confirmed against a real AnyGatewayREST + Command deployment - github issue #2).
            _logger.LogInformation("MarkMonitor order {OrderId} status 'CREATED' - pending external validation",
                order.Id);
            _logger.MethodExit();
            return (int)EndEntityStatus.EXTERNALVALIDATION;
        }

        _logger.LogError("MarkMonitor order {OrderId} status could not be dettermined defaulting to 'FAILED'",
            order.Id);
        _logger.MethodExit();
        return (int)EndEntityStatus.FAILED;
    }
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

/// <summary>Thrown when MarkMonitor's order-create response indicates success (2xx) but its body
/// can't be parsed - the order was definitely created despite the failure, so EnrollCertificateAsync
/// must treat this the same as an ambiguous network-level failure, not as a definite non-creation
/// that's safe to let a retry duplicate.</summary>
public class MarkMonitorOrderCreatedButUnparsableException : Exception
{
    public MarkMonitorOrderCreatedButUnparsableException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}