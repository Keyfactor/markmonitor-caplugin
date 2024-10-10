using System.ComponentModel;
using System.Reflection;


namespace Keyfactor.Extensions.CAPlugin.MarkMonitor.Models;

public enum CertificateEnrollmentType
{
    DV_SSL, // Domain Validated Secure Sockets Layer SSL certificate validated using domain name only
    DV_WILDCARD_SSL, // SSL certificate containing subdomains which is validated using domain name only
    EV_SSL, // Extended Validation SSL certificate validated using organization information, domain name, business legal status, and other factors
    OV_CS, // Code signing SSL certificate used by software developers to digitally sign apps. Validated using organization information
    OV_DS, // Driver signing SSL certificate used by software developers to digitally sign secure code for Windows hardware drivers. Validated using organization information
    OV_SSL, // SSL certificate validated using organization information and domain name
    OV_WILDCARD_SSL, // SSL certificate containing subdomains which is validated using organization information and domain name
    UCC_DV_SSL, // Unified Communication Certificate Multi domain SSL certificate validated using domain name only
    UCC_EV_SSL, // Multi domain SSL certificate validated using organization information, domain name, business legal status, and other factors
    UCC_OV_SSL // Multi domain SSL certificate validated using organization information and domain name
}

public enum OrderActions
{
    // https://api.markmonitor.com/certs/swagger/ssl-certs-api.html#/order/actionPatch
    [Description("cancel")] Cancel,
    
    [Description("reissue")] Reissue,
    
    [Description("revoke")] Revoke,
    
    [Description("sendCertificateEmail")] SendCertificateEmail,
    
    [Description("sendDcvEmail")] SendDcvEmail,
    
    [Description("validateDomains")] ValidateDomains,
    
    [Description("updateAdditionalEmails")] UpdateAdditionalEmails
}

public enum OrderStatus
{
    
    // https://api.markmonitor.com/certs/swagger/ssl-certs-api.html#/order/get_orders
    [Description("REISSUE_REQUEST_PENDING")]
    ReissueRequestPending,

    [Description("CREATED")] Created,

    [Description("REISSUE_PENDING")] ReissuePending,

    [Description("DIGI_PENDING")] DigiPending,

    [Description("DIGI_REISSUE_PENDING")] DigiReissuePending,

    [Description("DIGI_REISSUE_FAILED")] DigiReissueFailed,

    [Description("DIGI_PROCESSING")] DigiProcessing,

    [Description("DIGI_ISSUED")] DigiIssued,

    [Description("DIGI_REVOKED")] DigiRevoked,

    [Description("DIGI_CANCELED")] DigiCanceled,

    [Description("DIGI_NEEDS_CSR")] DigiNeedsCsr,

    [Description("DIGI_NEEDS_APPROVAL")] DigiNeedsApproval,

    [Description("DIGI_WAITING_PICKUP")] DigiWaitingPickup,

    [Description("DIGI_REJECTED")] DigiRejected,

    [Description("DIGI_EXPIRED")] DigiExpired,

    [Description("DIGI_FAILED")] DigiFailed
}

public static class EnumExtensions
{
    public static string GetDescription(this Enum value)
    {
        var field = value.GetType().GetField(value.ToString());
        var attribute = field.GetCustomAttribute<DescriptionAttribute>();
        return attribute?.Description ?? value.ToString();
    }
}

// Usage