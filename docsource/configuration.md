## Overview


## Requirements

- Markmonitor API Key (contact Markmonitor support for this)
- Markmonitor Service Account (username and password) w/ permission to create certificate orders
- Keyfactor Command >= v12.0.0
- AnyCA Gateway REST Portal >= v24.2.0

## Gateway Registration

In order to enroll for certificates the Keyfactor Command server must trust the trust chain. Markmonitor's default 
issuing CA (provider) is DigiCert, make sure to download and import the appropriate certificate chain from 
https://www.digicert.com/kb/digicert-root-certificates.htm to the AnyCA Gateway host.

Once the necessary files are copied to the appropriate locations and the AnyCA Gateway Rest is up and running, navigate 
to the AnyCA Gateway Rest portal and configure the CA.

### Using file path for issuing CA certificate
![gateway_registration_local_file.png](docsource/images/gateway_registration_local_file.png)

### Using Keyfactor Command certificate store for issuing CA certificate
> **⚠️ Warning:** The cert store must already exist in the Keyfactor Command.
![gateway_registration_store.png](docsource/images/gateway_registration_store.png)

## CA Connection Configuration
![gateway_ca_configuration.png](docsource/images/gateway_ca_configuration.png)

## Certificate Template Creation Step
A template must be created in Keyfactor Command to be used for certificate enrollment. One template is required for each
certificate product type supported by Markmonitor. Below is an example of a template for a `GeoTrust DV SSL certificate`.
For more on certificate product types contact your Markmonitor administrator or support.
![gateway_template.png](docsource/images/gateway_template.png)

## Custom Enrollment Parameter Creation Step

Custom enrollment parameters can be added to templates in Keyfactor Command after they have been imported from the AnyCA 
Gateway. All parameters are optional. Valid parameters:

| Parameter Name     | Description | Type |
|--------------------|------------|------|
| `AdditionalEmails` | List of 0 or more comma separated email addresses to send the certificate to via email after generation. | String |
| `MarkmonitorGroup` | The name or GUID of a Markmonitor group to use for the certificate request. | String |
| `MarkmonitorContact`| The name or GUID of a Markmonitor contact to use for the certificate request. Will use default Markmonitor organization contact if not specified. | String |
| `DCVMethod`        | The method to use for Domain Control Validation (DCV). Valid values are `EMAIL, DNS_CNAME_TOKEN, HTTP_TOKEN, DNS_TXT_TOKEN`. Default is `EMAIL`. | String |

![template_enrollment_params.png](docsource/images/template_enrollment_params.png)



