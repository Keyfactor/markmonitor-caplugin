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

public enum CertOrderTypes
{
    //SSL_OV_BASIC, SSL_EV_BASIC, SSL_DV_GEOTRUST, SSL_DV_THAWTE, SSL_OV_THAWTE_WEBSERVER, SSL_EV_THAWTE_WEBSERVER, SSL_OV_GEOTRUST_TRUEBIZID, SSL_EV_GEOTRUST_TRUEBIZID, SSL_OV_SECURESITE, SSL_EV_SECURESITE, SSL_OV_SECURESITE_PRO, SSL_EV_SECURESITE_PRO
    [Description("SSL_OV_BASIC")] SslOvBasic,

    [Description("SSL_EV_BASIC")] SslEvBasic,

    [Description("SSL_DV_GEOTRUST")] SslDvGeotrust,

    [Description("SSL_DV_THAWTE")] SslDvThawte,

    [Description("SSL_OV_THAWTE_WEBSERVER")]
    SslOvThawteWebserver,

    [Description("SSL_EV_THAWTE_WEBSERVER")]
    SslEvThawteWebserver,

    [Description("SSL_OV_GEOTRUST_TRUEBIZID")]
    SslOvGeotrustTruebizid,

    [Description("SSL_EV_GEOTRUST_TRUEBIZID")]
    SslEvGeotrustTruebizid,

    [Description("SSL_OV_SECURESITE")] SslOvSecuresite,

    [Description("SSL_EV_SECURESITE")] SslEvSecuresite,

    [Description("SSL_OV_SECURESITE_PRO")] SslOvSecuresitePro,

    [Description("SSL_EV_SECURESITE_PRO")] SslEvSecuresitePro
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

    [Description("updateAdditionalEmails")]
    UpdateAdditionalEmails
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

public enum AlgorithmTypes
{
    //RSA, ECC, DSA
    [Description("RSA")] Rsa,

    [Description("ECC")] Ecc,

    [Description("DSA")] Dsa
}

public enum CertServerPlatforms
{
    //APACHE, NGINX, DEFAULT, MICROSOFT_IIS_10, MICROSOFT_IIS_8, MICROSOFT_IIS_7, MICROSOFT_IIS_5_OR_6
    [Description("APACHE")] Apache,

    [Description("NGINX")] Nginx,

    [Description("DEFAULT")] Default,

    [Description("MICROSOFT_IIS_10")] MicrosoftIis10,

    [Description("MICROSOFT_IIS_8")] MicrosoftIis8,

    [Description("MICROSOFT_IIS_7")] MicrosoftIis7,

    [Description("MICROSOFT_IIS_5_OR_6")] MicrosoftIis5Or6
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