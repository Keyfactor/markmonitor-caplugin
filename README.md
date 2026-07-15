<h1 align="center" style="border-bottom: none">
    Markmonitor   Gateway AnyCA Gateway REST Plugin
</h1>

<p align="center">
  <!-- Badges -->
<img src="https://img.shields.io/badge/integration_status-pilot-3D1973?style=flat-square" alt="Integration Status: pilot" />
<a href="https://github.com/Keyfactor/markmonitor-cagateway/releases"><img src="https://img.shields.io/github/v/release/Keyfactor/markmonitor-cagateway?style=flat-square" alt="Release" /></a>
<img src="https://img.shields.io/github/issues/Keyfactor/markmonitor-cagateway?style=flat-square" alt="Issues" />
<img src="https://img.shields.io/github/downloads/Keyfactor/markmonitor-cagateway/total?style=flat-square&label=downloads&color=28B905" alt="GitHub Downloads (all assets, all releases)" />
</p>

<p align="center">
  <!-- TOC -->
  <a href="#support">
    <b>Support</b>
  </a> 
  ·
  <a href="#requirements">
    <b>Requirements</b>
  </a>
  ·
  <a href="#installation">
    <b>Installation</b>
  </a>
  ·
  <a href="#license">
    <b>License</b>
  </a>
  ·
  <a href="https://github.com/orgs/Keyfactor/repositories?q=anycagateway">
    <b>Related Integrations</b>
  </a>
</p>




## Compatibility

The Markmonitor   Gateway AnyCA Gateway REST plugin is compatible with the Keyfactor AnyCA Gateway REST 24.2.0 and later.

## Support
The Markmonitor   Gateway AnyCA Gateway REST plugin is supported by Keyfactor for Keyfactor customers. If you have a support issue, please open a support ticket with your Keyfactor representative. If you have a support issue, please open a support ticket via the Keyfactor Support Portal at https://support.keyfactor.com. 

> To report a problem or suggest a new feature, use the **[Issues](../../issues)** tab. If you want to contribute actual bug fixes or proposed enhancements, use the **[Pull requests](../../pulls)** tab.

## Requirements

- Markmonitor API Key (contact Markmonitor support for this)
- Markmonitor Service Account (username and password) w/ permission to create certificate orders
- Keyfactor Command >= v12.0.0
- AnyCA Gateway REST Portal >= v24.2.0

## Installation

1. Install the AnyCA Gateway REST per the [official Keyfactor documentation](https://software.keyfactor.com/Guides/AnyCAGatewayREST/Content/AnyCAGatewayREST/InstallIntroduction.htm).

2. On the server hosting the AnyCA Gateway REST, download and unzip the latest [Markmonitor   Gateway AnyCA Gateway REST plugin](https://github.com/Keyfactor/markmonitor-cagateway/releases/latest) from GitHub.

3. Copy the unzipped directory (usually called `net6.0`) to the Extensions directory:

    ```shell
    Program Files\Keyfactor\AnyCA Gateway\AnyGatewayREST\net6.0\Extensions
    ```

    > The directory containing the Markmonitor   Gateway AnyCA Gateway REST plugin DLLs (`net6.0`) can be named anything, as long as it is unique within the `Extensions` directory.

4. Restart the AnyCA Gateway REST service.

5. Navigate to the AnyCA Gateway REST portal and verify that the Gateway recognizes the Markmonitor   Gateway plugin by hovering over the ⓘ symbol to the right of the Gateway on the top left of the portal.

## Configuration

1. Follow the [official AnyCA Gateway REST documentation](https://software.keyfactor.com/Guides/AnyCAGatewayREST/Content/AnyCAGatewayREST/AddCA-Gateway.htm) to define a new Certificate Authority, and use the notes below to configure the **Gateway Registration** and **CA Connection** tabs:

    * **Gateway Registration**

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

    * **CA Connection**

        Populate using the configuration fields collected in the [requirements](#requirements) section.

        * **ApiKey** - The API Key for the MarkMonitor API 
        * **Username** - Username for the MarkMonitor API service account 
        * **Password** - Password for the MarkMonitor API service account 
        * **BaseUrl** - The Base URL for the MarkMonitor API - Usually either https://api.markmonitor.com 
        * **OrgId** - The name of the MarkMonitor Organization to use for the API calls (ex: MarkMonitor). You can also use the Organization ID in GUID format. 
        * **Enabled** - Flag to Enable or Disable gateway functionality. Disabling is primarily used to allow creation of the CA prior to configuration information being available. 

2. A template must be created in Keyfactor Command to be used for certificate enrollment. One template is required for each
    certificate product type supported by Markmonitor. Below is an example of a template for a `GeoTrust DV SSL certificate`.
    For more on certificate product types contact your Markmonitor administrator or support.
    ![gateway_template.png](docsource/images/gateway_template.png)

3. Follow the [official Keyfactor documentation](https://software.keyfactor.com/Guides/AnyCAGatewayREST/Content/AnyCAGatewayREST/AddCA-Keyfactor.htm) to add each defined Certificate Authority to Keyfactor Command and import the newly defined Certificate Templates.

4. Custom enrollment parameters can be added to templates in Keyfactor Command after they have been imported from the AnyCA 
    Gateway. All parameters are optional. Valid parameters:

    | Parameter Name     | Description | Type |
    |--------------------|------------|------|
    | `AdditionalEmails` | List of 0 or more comma separated email addresses to send the certificate to via email after generation. | String |
    | `MarkmonitorGroup` | The name or GUID of a Markmonitor group to use for the certificate request. | String |
    | `MarkmonitorContact`| The name or GUID of a Markmonitor contact to use for the certificate request. Will use default Markmonitor organization contact if not specified. | String |
    | `DCVMethod`        | The method to use for Domain Control Validation (DCV). Valid values are `EMAIL, DNS_CNAME_TOKEN, HTTP_TOKEN, DNS_TXT_TOKEN`. Default is `EMAIL`. | String |

    ![template_enrollment_params.png](docsource/images/template_enrollment_params.png)


## CA Connection Configuration
![gateway_ca_configuration.png](docsource/images/gateway_ca_configuration.png)


## License

Apache License 2.0, see [LICENSE](LICENSE).

## Related Integrations

See all [Keyfactor Any CA Gateways (REST)](https://github.com/orgs/Keyfactor/repositories?q=anycagateway).