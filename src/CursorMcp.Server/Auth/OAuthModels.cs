using System.Text.Json.Serialization;

namespace CursorMcp.Server.Auth;

public sealed class OAuthClient
{
    public required string ClientId { get; init; }
    public string? ClientSecret { get; init; }
    public bool RequiresSecret { get; init; }
    public List<string> RedirectUris { get; init; } = [];
}

public sealed class LoginTicket
{
    public required string Ticket { get; init; }
    public required string ClientId { get; init; }
    public required string RedirectUri { get; init; }
    public required string CodeChallenge { get; init; }
    public string? State { get; init; }
    public string? Resource { get; init; }
    public string Scope { get; init; } = "mcp:tools";
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class AuthorizationCode
{
    public required string Code { get; init; }
    public required string ClientId { get; init; }
    public required string RedirectUri { get; init; }
    public required string CodeChallenge { get; init; }
    public string? Resource { get; init; }
    public string Scope { get; init; } = "mcp:tools";
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed class TokenResponse
{
    [JsonPropertyName("access_token")]
    public required string AccessToken { get; init; }

    [JsonPropertyName("token_type")]
    public string TokenType { get; init; } = "Bearer";

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; init; }

    [JsonPropertyName("scope")]
    public string? Scope { get; init; }
}

public sealed class OAuthErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}

public sealed class ClientRegistrationRequest
{
    [JsonPropertyName("redirect_uris")]
    public List<string> RedirectUris { get; set; } = [];

    [JsonPropertyName("token_endpoint_auth_method")]
    public string? TokenEndpointAuthMethod { get; set; }

    [JsonPropertyName("grant_types")]
    public List<string>? GrantTypes { get; set; }

    [JsonPropertyName("response_types")]
    public List<string>? ResponseTypes { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }
}

public sealed class ClientRegistrationResponse
{
    [JsonPropertyName("client_id")]
    public required string ClientId { get; init; }

    [JsonPropertyName("client_id_issued_at")]
    public long ClientIdIssuedAt { get; init; }

    [JsonPropertyName("redirect_uris")]
    public required List<string> RedirectUris { get; init; }

    [JsonPropertyName("grant_types")]
    public string[] GrantTypes { get; init; } = ["authorization_code"];

    [JsonPropertyName("response_types")]
    public string[] ResponseTypes { get; init; } = ["code"];

    [JsonPropertyName("token_endpoint_auth_method")]
    public string TokenEndpointAuthMethod { get; init; } = "none";
}
