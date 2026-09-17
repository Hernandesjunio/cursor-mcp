namespace CursorMcp.Server.Auth;

public sealed class SpikeAuthOptions
{
    public const string SectionName = "SpikeAuth";

    public string PublicBaseUrl { get; set; } = "http://localhost:7071";
    public string SigningKey { get; set; } = "spike-demo-hmac-key-32-bytes-min!!";
    public int AccessTokenMinutes { get; set; } = 15;
    public int AuthorizationCodeMinutes { get; set; } = 5;
    public string Scope { get; set; } = "mcp:tools";
    public string DemoUser { get; set; } = "demo-user";
    public string DemoClientId { get; set; } = "cursor-mcp-spike";

    public string Issuer => PublicBaseUrl.TrimEnd('/');
    public string Resource => $"{Issuer}/mcp";
}
