using System.Text.Json.Serialization;

namespace CursorMcp.Server.Auth;

public sealed class OAuthClient
{
    public required string ClientId { get; init; }
    public string Name { get; init; } = "";
    public string? ClientSecret { get; init; }
    public bool RequiresSecret { get; init; }
    public List<string> RedirectUris { get; init; } = [];

    public bool AllowsRedirectUri(string? redirectUri)
    {
        if (!Uri.TryCreate(redirectUri, UriKind.Absolute, out var requested))
        {
            return false;
        }

        foreach (var registered in RedirectUris)
        {
            if (string.Equals(registered, redirectUri, StringComparison.Ordinal))
            {
                return true;
            }

            if (!Uri.TryCreate(registered, UriKind.Absolute, out var allowed))
            {
                continue;
            }

            if (IsLoopback(requested) && IsLoopback(allowed) &&
                string.Equals(requested.Scheme, allowed.Scheme, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(requested.Host, allowed.Host, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(requested.AbsolutePath, allowed.AbsolutePath, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsLoopback(Uri uri) =>
        uri.IsLoopback || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
}

public sealed class LoginTicket
{
    public required string Ticket { get; init; }
    public required string ClientId { get; init; }
    public required string ClientName { get; init; }
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

public sealed class RefreshToken
{
    public required string Token { get; init; }
    public required string FamilyId { get; init; }
    public required string ClientId { get; init; }
    public required string Audience { get; init; }
    public required string Scope { get; init; }
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

    [JsonPropertyName("refresh_token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RefreshToken { get; init; }
}

public sealed class OAuthErrorResponse
{
    [JsonPropertyName("error")]
    public required string Error { get; init; }

    [JsonPropertyName("error_description")]
    public string? ErrorDescription { get; init; }
}
