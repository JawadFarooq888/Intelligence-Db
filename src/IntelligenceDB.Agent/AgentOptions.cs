namespace IntelligenceDB.Agent;

/// <summary>appsettings section "Agent" (written by install-agent.ps1).</summary>
public sealed class AgentOptions
{
    public const string SectionName = "Agent";

    /// <summary>Portal address, e.g. https://intelligencedb.contoso.com</summary>
    public string PortalUrl { get; set; } = string.Empty;

    public string AgentId { get; set; } = string.Empty;

    /// <summary>The key shown once when the agent was created in the portal.</summary>
    public string AgentKey { get; set; } = string.Empty;

    /// <summary>How many SQL Servers are collected at the same time.</summary>
    public int MaxParallelServers { get; set; } = 8;

    /// <summary>
    /// Only for development: allow http:// portal URLs. The portal sends connection secrets to the agent,
    /// so production must use https://.
    /// </summary>
    public bool AllowInsecureHttp { get; set; }

    public bool IsValid(out string error)
    {
        if (!Uri.TryCreate(PortalUrl, UriKind.Absolute, out var uri)) { error = "Agent:PortalUrl is missing or not a URL."; return false; }
        if (uri.Scheme != Uri.UriSchemeHttps && !(AllowInsecureHttp && uri.Scheme == Uri.UriSchemeHttp))
        {
            error = "Agent:PortalUrl must use https:// (set Agent:AllowInsecureHttp=true only for local testing).";
            return false;
        }

        if (!Guid.TryParse(AgentId, out _)) { error = "Agent:AgentId is missing or not a GUID."; return false; }
        if (string.IsNullOrWhiteSpace(AgentKey)) { error = "Agent:AgentKey is missing."; return false; }
        error = string.Empty;
        return true;
    }
}
