using Newtonsoft.Json;

namespace Keyfactor.Extensions.CAPlugin.MarkMonitor;

public class MarkMonitorConfig
{
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.ApiKey)]
    public string ApiKey { get; set; }
        
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.ApiPassword)]
    public string ApiPassword { get; set; }
        
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.ApiUsername)]
    public string ApiUsername { get; set; }
        
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.BaseUrl)]
    public string BaseUrl { get; set; }
        
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.OrgName)]
    public string OrgName { get; set; }
        
    [JsonProperty(MarkMonitorCAPluginConfig.ConfigConstants.Enabled)]
    public bool Enabled { get; set; }
    
    public string MarkMonitorApiClient { get; set; }
}