namespace CursorMcp.Server.Auth;

public sealed class SpikeAuthOptions
{
    public const string SectionName = "SpikeAuth";

    public string PublicBaseUrl { get; set; } = "https://localhost:7071";
    public string SigningKey { get; set; } = "spike-demo-hmac-key-32-bytes-min!!";
    public int AccessTokenMinutes { get; set; } = 15;
    public int AuthorizationCodeMinutes { get; set; } = 5;
    public int RefreshTokenDays { get; set; } = 7;
    public string Scope { get; set; } = "mcp:tools";
    public string DemoUser { get; set; } = "demo-user";
    public List<RegisteredClientOptions> Clients { get; set; } = [];

    public string Issuer => PublicBaseUrl.TrimEnd('/');
    public string Resource => $"{Issuer}/mcp";
    public string DemoClientId => Clients.Count > 0 ? Clients[0].ClientId : "cursor-mcp-spike";
}

public sealed class RegisteredClientOptions
{
    public string ClientId { get; set; } = "";
    public string Name { get; set; } = "";
    public List<string> RedirectUris { get; set; } = [];
    public bool RequiresSecret { get; set; }
    public string? ClientSecret { get; set; }
}
