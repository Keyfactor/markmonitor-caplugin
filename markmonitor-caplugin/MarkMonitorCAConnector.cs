// Copyright 2025 Keyfactor
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

using System.Collections.Concurrent;
using Keyfactor.AnyGateway.Extensions;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Client;
using Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;
using Keyfactor.Logging;
using Keyfactor.PKI.Enums.EJBCA;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using MarkMonitorConstants = Keyfactor.Extensions.CAPlugin.MarkMonitor.MarkMonitorCAPluginConfig;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor;

public class MarkMonitorCAPlugin : IAnyCAPlugin
{
    private readonly ILogger _logger = LogHandler.GetClassLogger<MarkMonitorCAPlugin>();
    private ICertificateDataReader _certificateDataReader;
    private MarkMonitorConfig _config;
    private MarkMonitorClient Client;
    private bool _markMonitorClientWasInjected = false;
    private MarkMonitorClient _cachedClient;
    private readonly SemaphoreSlim _clientLock = new(1, 1);

    public MarkMonitorCAPlugin()
    {
        // Explicit default constructor
    }

    public MarkMonitorCAPlugin(MarkMonitorClient client)
    {
        Client = client;
        _markMonitorClientWasInjected = true;
    }

    public void Initialize(IAnyCAPluginConfigProvider configProvider, ICertificateDataReader certificateDataReader)
    {
        _logger.MethodEntry();
        _certificateDataReader = certificateDataReader;
        var rawConfig = JsonConvert.SerializeObject(configProvider.CAConnectionData);
        _config = JsonConvert.DeserializeObject<MarkMonitorConfig>(rawConfig);
        // _logger.LogTrace("MarkMonitorCAPlugin initialized with config: {Config}", rawConfig);
        _logger.MethodExit();
    }


    private void logConfig()
    {
        _logger.MethodEntry();
        _logger.LogInformation("MarkMonitorCAPlugin config baseUrl: {Config}", _config.BaseUrl);
        LogMaskedConfigValue("apiKey", _config.ApiKey);
        LogMaskedConfigValue("apiUsername", _config.ApiUsername);
        LogMaskedConfigValue("apiPassword", _config.ApiPassword);
        _logger.LogInformation("MarkMonitorCAPlugin config orgName: {Config}", _config.OrgName);
        _logger.MethodExit();
    }

    private void LogMaskedConfigValue(string label, string value)
    {
        if (value is { Length: > 0 })
            _logger.LogInformation("MarkMonitorCAPlugin config {Label}: {Config}", label, new string('*', 32));
        else
            _logger.LogError("MarkMonitorCAPlugin config {Label}: NOT SET", label);
    }

    public async Task<AnyCAPluginCertificate> GetSingleRecord(string caRequestId)
    {
        _logger.MethodEntry();
        try
        {
            var client = await CreateAndAuthenticateClientAsync();
            _logger.LogInformation("Getting order details for CARequestID: {CARequestID}", caRequestId);
            var order = await client.GetSingleOrderAsync(caRequestId);
            _logger.LogInformation("Order details retrieved for CARequestID: {CARequestID}", caRequestId);
            return order;
        }
        catch (Exception e)
        {
            _logger.LogError("Failed to get order details for CARequestID {CARequestID}: {EMessage}", caRequestId,
                e.Message);
            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
        
    }

    public async Task Synchronize(BlockingCollection<AnyCAPluginCertificate> blockingBuffer, DateTime? lastSync,
        bool fullSync, CancellationToken cancelToken)
    {
        _logger.MethodEntry();

        try
        {
            _logger.LogInformation(fullSync
                ? "Performing a full CA synchronization"
                : "Performing a partial CA synchronization");

            logConfig();

            _logger.LogDebug("Calling CreateAndAuthenticateClientAsync");
            var client = await CreateAndAuthenticateClientAsync();
            _logger.LogDebug("CreateAndAuthenticateClientAsync completed");

            _logger.LogInformation("Attempting to synchronize certificates with MarkMonitor API");
            var certificates = await client.GetCertificateInventoryAsync("", "", 100, blockingBuffer, cancelToken);
            _logger.LogDebug("Synchronized {Certificates} certificates", certificates);

            // Check for cancellation after operation
            // cancelToken.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Synchronization canceled");
            throw; // Rethrow the cancellation exception to ensure it's propagated
        }
        catch (Exception ex)
        {
            _logger.LogError("An error occurred during synchronization: {ExMessage}", ex.Message);
            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    public async Task<int> Revoke(string orderId, string hexSerialNumber, uint revocationReason)
    {
        _logger.MethodEntry();
        try
        {
            EnsureOrgNameConfigured();

            _logger.LogInformation(
                "Revoking certificate with CARequestID: {CaRequestId}, SerialNumber: {HexSerialNumber}, Reason: {RevocationReason}",
                orderId, hexSerialNumber, revocationReason);

            var client = await CreateAndAuthenticateClientAsync();

            _logger.LogInformation("Attempting to revoke certificate with CARequestID: {CaRequestId}", orderId);

            var revokeResult = await client.RevokeCertificateAsync(orderId, _config.OrgName, revocationReason);

            if (revokeResult) return (int)EndEntityStatus.REVOKED;

            throw new Exception("Unable to revoke certificate associated with order ID: " + orderId);
        }
        catch (Exception e)
        {
            _logger.LogError("Revoke failed for order {OrderId}: {EMessage}", orderId, e.Message);
            throw new Exception($"Revoke Failed with message {e.Message}");
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    public async Task<EnrollmentResult> Enroll(string csr, string subject, Dictionary<string, string[]> san,
        EnrollmentProductInfo productInfo, RequestFormat requestFormat, EnrollmentType enrollmentType)
    {
        _logger.MethodEntry();
        // Subject is fully requester-controlled (it comes straight off the submitted CSR) - log a
        // CR/LF-escaped copy everywhere below so an embedded CR/LF can't forge a fake log line
        // (CWE-117). The real, unsanitized subject is still what's actually used for enrollment.
        var logSafeSubject = LogSanitizer.ForLog(subject);
        try
        {
            _logger.LogInformation("Enrolling certificate `{Subject}` with MarkMonitor", logSafeSubject);

            var client = await CreateAndAuthenticateClientAsync();

            _logger.LogInformation("Performing an Enrollment");
            _logger.LogTrace("CSR: {Csr}", csr);
            _logger.LogTrace("Subject: {Subject}", logSafeSubject);
            _logger.LogTrace("SAN: {San}", JsonConvert.SerializeObject(san));
            _logger.LogTrace("Product ID: {ProductId}", productInfo.ProductID);

            var enrollResult = await client.EnrollCertificateAsync(csr, subject, san, productInfo.ProductID,
                productInfo.ProductParameters, _config);

            _logger.LogTrace("Enrollment result: {EnrollResult}", JsonConvert.SerializeObject(enrollResult));
            _logger.LogInformation("Enrollment completed successfully for subject: {Subject}", logSafeSubject);

            if (enrollmentType == EnrollmentType.RenewOrReissue)
                await RevokePriorCertificateIfPresentAsync(client, productInfo, subject, enrollResult.CARequestID);

            return enrollResult;
        }
        catch (Exception e)
        {
            _logger.LogError("Enrollment failed for subject {Subject}: {EMessage}", logSafeSubject, e.Message);
            throw;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    /// <summary>
    /// For a RenewOrReissue enrollment, the AnyGateway core framework passes the prior
    /// certificate's serial number in productInfo.ProductParameters["PriorCertSN"]. Resolves it to a
    /// CARequestID via the injected ICertificateDataReader and revokes it now that the replacement
    /// certificate has issued successfully. Falls back to treating the enrollment as a plain new
    /// issuance (no revoke attempted) if PriorCertSN is missing or can't be resolved - a failure to
    /// revoke the old certificate should not fail delivery of the new one.
    /// </summary>
    private async Task RevokePriorCertificateIfPresentAsync(MarkMonitorClient client,
        EnrollmentProductInfo productInfo, string subject, string newCaRequestId)
    {
        var priorCertSn = productInfo.ProductParameters?
            .FirstOrDefault(kv => string.Equals(kv.Key, "PriorCertSN", StringComparison.OrdinalIgnoreCase)).Value;

        if (string.IsNullOrWhiteSpace(priorCertSn))
        {
            _logger.LogWarning(
                "Enrollment for {Subject} was requested as RenewOrReissue but no PriorCertSN was provided - treating it as a new enrollment",
                LogSanitizer.ForLog(subject));
            return;
        }

        var priorRequestId = await _certificateDataReader.GetRequestIDBySerialNumber(priorCertSn);
        if (string.IsNullOrWhiteSpace(priorRequestId))
        {
            _logger.LogWarning(
                "Could not resolve a CARequestID for PriorCertSN {PriorCertSn} - the prior certificate will not be revoked",
                priorCertSn);
            return;
        }

        try
        {
            EnsureOrgNameConfigured();

            _logger.LogInformation(
                "Revoking prior certificate {PriorRequestId} (serial {PriorCertSn}) after it was replaced by {NewCaRequestId}",
                priorRequestId, priorCertSn, newCaRequestId);
            await client.RevokeCertificateAsync(priorRequestId, _config.OrgName);
        }
        catch (Exception e)
        {
            _logger.LogError(
                "Failed to revoke prior certificate {PriorRequestId} after it was replaced by {NewCaRequestId}: {EMessage}",
                priorRequestId, newCaRequestId, e.Message);
        }
    }

    /// <summary>
    /// RevokeCertificateAsync's cross-org ownership check is skipped (not rejected) when its
    /// orgName parameter is blank - a deliberate allowance for ad-hoc/manual callers that don't
    /// scope by organization. Initialize() never re-validates the deserialized config, so without
    /// this guard a plugin instance loaded with a blank OrgId would silently revoke without any
    /// organization check at all, on every Revoke call this connector makes.
    /// </summary>
    private void EnsureOrgNameConfigured()
    {
        if (string.IsNullOrWhiteSpace(_config.OrgName))
            throw new ConfigurationValidationException(
                "MarkMonitor OrgId is not configured - refusing to revoke without an organization to verify ownership against");
    }

    public async Task Ping()
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Attempting to authenticate with MarkMonitor API");
            var client = await CreateAndAuthenticateClientAsync();

            if (client == null) throw new Exception("Error attempting to ping MarkMonitor");

            _logger.LogInformation("Authentication with MarkMonitor API successful");

            _logger.LogInformation("Attempting to list organizations");
            var orgs = await client.ListOrganizationsAsync();

            if (orgs == null || !orgs.Any())
                throw new Exception("Unable to ping MarkMonitor API, or no MarkMonitor organization exist");
            _logger.LogInformation("Successfully pinged MarkMonitor API");
        }
        catch (Exception e)
        {
            _logger.LogError("There was an error contacting MarkMonitor: {EMessage}", e.Message);
            throw new Exception($"Error attempting to ping MarkMonitor: {e.Message}.", e);
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    public async Task ValidateCAConnectionInfo(Dictionary<string, object> connectionInfo)
    {
        _logger.MethodEntry();
        _logger.LogInformation("Validating CA Connection Info");

        var errors = new List<string>();

        _logger.LogDebug("Checking the API Key");
        var apiKey = connectionInfo.TryGetValue(MarkMonitorConstants.ConfigConstants.ApiKey, out var aKey)
            ? (string)aKey
            : string.Empty;
        if (string.IsNullOrWhiteSpace(apiKey))
            errors.Add($"A valid `{MarkMonitorConstants.ConfigConstants.ApiKey} is required");
        else _logger.LogDebug($"{MarkMonitorConstants.ConfigConstants.ApiKey} is set");

        _logger.LogDebug("Checking the API service account password");
        var apiPassword = connectionInfo.TryGetValue(MarkMonitorConstants.ConfigConstants.ApiPassword, out var aPass)
            ? (string)aPass
            : string.Empty;
        if (string.IsNullOrWhiteSpace(apiPassword))
            errors.Add($"A valid service account `{MarkMonitorConstants.ConfigConstants.ApiPassword}` is required");
        else _logger.LogDebug($"{MarkMonitorConstants.ConfigConstants.ApiPassword} is set");

        _logger.LogDebug("Checking the API service account username");
        var apiUsername = connectionInfo.TryGetValue(MarkMonitorConstants.ConfigConstants.ApiUsername, out var aUser)
            ? (string)aUser
            : string.Empty;
        if (string.IsNullOrWhiteSpace(apiUsername))
            errors.Add($"A valid service account `{MarkMonitorConstants.ConfigConstants.ApiUsername}` is required");
        else _logger.LogDebug($"{MarkMonitorConstants.ConfigConstants.ApiUsername} is set");

        _logger.LogDebug("Checking the API base URL");
        var baseURL = connectionInfo.TryGetValue(MarkMonitorConstants.ConfigConstants.BaseUrl, out var aUrl)
            ? (string)aUrl
            : string.Empty;
        if (string.IsNullOrWhiteSpace(baseURL)) baseURL = "https://api.markmonitor.com";
        else if (!baseURL.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            errors.Add("The Base URL must start with https:// - credentials and the bearer token are sent to it");
        else _logger.LogDebug($"{MarkMonitorConstants.ConfigConstants.BaseUrl} is set");
        _logger.LogTrace("MarkMonitor API Base URL: {BaseURL}", baseURL);

        _logger.LogDebug("Checking the Organization Name");
        var orgName = connectionInfo.TryGetValue(MarkMonitorConstants.ConfigConstants.OrgName, out var aOrg)
            ? (string)aOrg
            : string.Empty;
        if (string.IsNullOrWhiteSpace(orgName)) errors.Add("A valid Organization name is required");
        else _logger.LogDebug($"{MarkMonitorConstants.ConfigConstants.OrgName} is set");
        _logger.LogTrace("MarkMonitor Organization Name: {OrgName}", orgName);
        if (errors.Any()) ThrowValidationException(errors);
        _logger.LogInformation("CA Connection Info validated successfully");
        _logger.MethodExit();
    }

    // Considered validating MarkmonitorContact/MarkmonitorGroup here eagerly (at template-save
    // time) instead of the current behavior - a typo'd value logs a warning and silently falls back
    // to a default at enroll time. Deferred: doing so would mean making live MarkMonitor API calls
    // during template save (no other Validate* method in this codebase does that), coupling template
    // configuration to MarkMonitor's availability/latency, and duplicating the resolution logic
    // that already lives in EnrollCertificateAsync. That's a real product tradeoff (fail fast on
    // template save vs. graceful degradation at enroll time) rather than a straightforward bug fix.
    public Task ValidateProductInfo(EnrollmentProductInfo productInfo, Dictionary<string, object> connectionInfo)
    {
        _logger.MethodEntry();
        _logger.LogInformation("Product info validated successfully");
        _logger.MethodExit();
        return Task.CompletedTask;
    }

    public Dictionary<string, PropertyConfigInfo> GetCAConnectorAnnotations()
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Retrieving CA Connector annotations");
            return MarkMonitorConstants.GetPluginAnnotations();
        }
        finally
        {
            _logger.MethodExit();   
        }
        
    }

    public Dictionary<string, PropertyConfigInfo> GetTemplateParameterAnnotations()
    {
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Retrieving template parameter annotations");
            return MarkMonitorConstants.GetTemplateParameterAnnotations();    
        }
        finally
        {
            _logger.MethodExit();
        }
        
    }

    public List<string> GetProductIds()
    {
        // return list of CertOrderTypes Enum values
        _logger.MethodEntry();
        try
        {
            _logger.LogInformation("Retrieving product IDs from CertOrderTypes Enum");
            return Enum.GetNames(typeof(CertOrderTypes)).ToList();    
        } 
        finally
        {
            _logger.MethodExit();
        }
        
    }

    /// <summary>
    /// Returns a single MarkMonitorClient shared for the lifetime of this plugin instance, building
    /// it (or adopting an injected one) on first use only. Each of MarkMonitorClient's own methods
    /// authenticates/re-authenticates itself lazily as needed, so this method does not need to - and
    /// deliberately does not - force an eager authentication call on every invocation.
    /// </summary>
    internal async Task<MarkMonitorClient> CreateAndAuthenticateClientAsync()
    {
        _logger.MethodEntry();
        try
        {
            if (_cachedClient != null) return _cachedClient;

            await _clientLock.WaitAsync();
            try
            {
                _cachedClient ??= _markMonitorClientWasInjected
                    ? Client
                    : new MarkMonitorClient(
                        _config.BaseUrl,
                        _config.ApiKey,
                        _config.ApiUsername,
                        _config.ApiPassword,
                        true
                    );
            }
            finally
            {
                _clientLock.Release();
            }

            return _cachedClient;
        }
        finally
        {
            _logger.MethodExit();
        }
    }

    private void ThrowValidationException(List<string> errors)
    {
        _logger.MethodEntry();
        var validationMsg = $"Validation errors:\n{string.Join("\n", errors)}";
        _logger.LogError("{Errors}",validationMsg);
        throw new AnyCAValidationException(validationMsg);
    }
}