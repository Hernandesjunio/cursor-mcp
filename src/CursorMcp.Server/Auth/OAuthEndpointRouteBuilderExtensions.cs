using System.Security.Cryptography;
using System.Text;
using CursorMcp.Server;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CursorMcp.Server.Auth;

public static class OAuthEndpointRouteBuilderExtensions
{
    public static IEndpointRouteBuilder MapSpikeOAuth(this IEndpointRouteBuilder app)
    {
        var oauth = app.MapGroup(string.Empty).WithTags("OAuth");

        oauth.MapGet("/.well-known/oauth-authorization-server", GetMetadata)
            .WithName("GetAuthorizationServerMetadata")
            .WithSummary("RFC 8414 OAuth authorization server metadata.");

        oauth.MapGet("/.well-known/openid-configuration", GetMetadata)
            .WithName("GetOpenIdConfiguration")
            .WithSummary("OpenID Provider metadata (same spike document as RFC 8414).")
            .ExcludeFromDescription();

        oauth.MapGet("/authorize", Authorize)
            .WithName("Authorize")
            .WithSummary("Starts the authorization code + PKCE flow and redirects to /login.");

        oauth.MapGet("/login", ShowLogin)
            .WithName("ShowLogin")
            .WithSummary("Local login page used to approve the Cursor callback.");

        oauth.MapPost("/login", CompleteLogin)
            .WithName("CompleteLogin")
            .WithSummary("Approves the demo user and redirects to the client callback.")
            .DisableAntiforgery()
            .ExcludeFromDescription();

        oauth.MapPost("/token", IssueToken)
            .WithName("Token")
            .WithSummary("Exchanges an authorization code for a JWT access token.")
            .Accepts<Dictionary<string, string>>("application/x-www-form-urlencoded")
            .Produces<TokenResponse>(StatusCodes.Status200OK)
            .Produces<OAuthErrorResponse>(StatusCodes.Status400BadRequest)
            .DisableAntiforgery();

        return app;
    }

    private static IResult GetMetadata(IOptions<SpikeAuthOptions> optionsAccessor)
    {
        var issuer = optionsAccessor.Value.Issuer;
        // #region agent log
        AgentDebugLog.Write("A", "OAuth:GetMetadata", "AS metadata served without DCR", new
        {
            issuer,
            hasRegistrationEndpoint = false,
            authorizationEndpoint = $"{issuer}/authorize",
            tokenEndpoint = $"{issuer}/token"
        });
        // #endregion
        return Results.Json(new
        {
            issuer,
            authorization_endpoint = $"{issuer}/authorize",
            token_endpoint = $"{issuer}/token",
            response_types_supported = new[] { "code" },
            grant_types_supported = new[] { "authorization_code" },
            code_challenge_methods_supported = new[] { "S256" },
            token_endpoint_auth_methods_supported = new[] { "none", "client_secret_post" },
            scopes_supported = new[] { optionsAccessor.Value.Scope },
            authorization_response_iss_parameter_supported = true
        });
    }

    private static IResult Authorize(
        OAuthStore store,
        IOptions<SpikeAuthOptions> optionsAccessor,
        [FromQuery(Name = "response_type")] string? responseType,
        [FromQuery(Name = "client_id")] string? clientId,
        [FromQuery(Name = "redirect_uri")] string? redirectUri,
        [FromQuery(Name = "state")] string? state,
        [FromQuery(Name = "code_challenge")] string? codeChallenge,
        [FromQuery(Name = "code_challenge_method")] string? codeChallengeMethod,
        [FromQuery(Name = "scope")] string? scope,
        [FromQuery(Name = "resource")] string? resource)
    {
        var options = optionsAccessor.Value;

        OAuthClient? resolvedClient = null;
        var clientFound = !string.IsNullOrWhiteSpace(clientId) && store.TryGetClient(clientId!, out resolvedClient!);
        var redirectAllowed = clientFound && resolvedClient!.AllowsRedirectUri(redirectUri);
        // #region agent log
        AgentDebugLog.Write("E", "OAuth:Authorize", "Authorize request received", new
        {
            runId = "post-fix",
            clientId,
            redirectUri,
            responseType,
            codeChallengeMethod,
            scope,
            resource,
            hasCodeChallenge = !string.IsNullOrWhiteSpace(codeChallenge),
            clientFound,
            redirectAllowed,
            registeredRedirectUris = clientFound ? resolvedClient!.RedirectUris.ToArray() : Array.Empty<string>()
        });
        // #endregion

        if (string.IsNullOrWhiteSpace(clientId))
        {
            return OAuthError("invalid_request", "client_id is required.");
        }

        if (!clientFound)
        {
            return OAuthError("unauthorized_client", "The client is not registered.");
        }

        var client = resolvedClient!;

        if (!redirectAllowed)
        {
            return OAuthError("invalid_request", "redirect_uri is not registered for this client.");
        }

        if (!string.Equals(responseType, "code", StringComparison.Ordinal))
        {
            return RedirectError(redirectUri!, "unsupported_response_type", "Only response_type=code is supported.", state);
        }

        if (string.IsNullOrWhiteSpace(codeChallenge) ||
            !string.Equals(codeChallengeMethod, "S256", StringComparison.Ordinal))
        {
            return RedirectError(redirectUri!, "invalid_request", "PKCE S256 code_challenge is required.", state);
        }

        if (!TryNormalizeResource(resource, options, out var normalizedResource))
        {
            return RedirectError(redirectUri!, "invalid_target", "The specified resource is not valid.", state);
        }

        var ticket = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        store.CreateTicket(new LoginTicket
        {
            Ticket = ticket,
            ClientId = client.ClientId,
            ClientName = client.Name,
            RedirectUri = redirectUri!,
            CodeChallenge = codeChallenge,
            State = state,
            Resource = normalizedResource,
            Scope = string.IsNullOrWhiteSpace(scope) ? options.Scope : scope,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
        });

        return Results.Redirect($"/login?ticket={Uri.EscapeDataString(ticket)}");
    }

    private static IResult ShowLogin(string? ticket, OAuthStore store)
    {
        if (string.IsNullOrWhiteSpace(ticket) || !store.TryPeekTicket(ticket, out var pending))
        {
            return Results.Content("<h1>Login expirado</h1><p>Reinicie a conexão MCP no Cursor.</p>", "text/html; charset=utf-8", statusCode: 400);
        }

        return Results.Content(LoginPage.Render(pending.Ticket, pending.ClientId, pending.ClientName), "text/html; charset=utf-8");
    }

    private static async Task<IResult> CompleteLogin(HttpContext http, OAuthStore store)
    {
        var form = await http.Request.ReadFormAsync();
        var ticket = form["ticket"].ToString();
        if (string.IsNullOrWhiteSpace(ticket) || !store.TryTakeTicket(ticket, out var pending))
        {
            return Results.Content("<h1>Login expirado</h1><p>Reinicie a conexão MCP no Cursor.</p>", "text/html; charset=utf-8", statusCode: 400);
        }

        var code = store.CreateCode(pending);
        var callback = QueryHelpers.AddQueryString(pending.RedirectUri, new Dictionary<string, string?>
        {
            ["code"] = code,
            ["state"] = pending.State,
            ["iss"] = http.RequestServices.GetRequiredService<IOptions<SpikeAuthOptions>>().Value.Issuer
        });

        return Results.Redirect(callback);
    }

    private static async Task<IResult> IssueToken(
        HttpContext http,
        OAuthStore store,
        JwtTokenIssuer issuer,
        IOptions<SpikeAuthOptions> optionsAccessor)
    {
        var form = await http.Request.ReadFormAsync();
        var options = optionsAccessor.Value;
        var grantType = form["grant_type"].ToString();
        var clientId = form["client_id"].ToString();
        var clientSecret = form["client_secret"].ToString();

        if (string.IsNullOrEmpty(clientId) || !store.TryGetClient(clientId, out var client))
        {
            return Results.Json(new OAuthErrorResponse
            {
                Error = "invalid_client",
                ErrorDescription = "The client is not registered."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (client.RequiresSecret && client.ClientSecret != clientSecret)
        {
            return Results.Json(new OAuthErrorResponse
            {
                Error = "invalid_client",
                ErrorDescription = "Invalid client credentials."
            }, statusCode: StatusCodes.Status401Unauthorized);
        }

        if (!string.Equals(grantType, "authorization_code", StringComparison.Ordinal))
        {
            return OAuthError("unsupported_grant_type", "Only authorization_code is supported.");
        }

        var code = form["code"].ToString();
        var redirectUri = form["redirect_uri"].ToString();
        var codeVerifier = form["code_verifier"].ToString();
        var resource = form["resource"].ToString();

        if (string.IsNullOrEmpty(code) || !store.TryTakeCode(code, out var codeInfo))
        {
            return OAuthError("invalid_grant", "Invalid authorization code.");
        }

        if (!string.Equals(codeInfo.ClientId, client.ClientId, StringComparison.Ordinal))
        {
            return OAuthError("invalid_grant", "Authorization code was not issued to this client.");
        }

        if (!string.IsNullOrEmpty(redirectUri) &&
            !string.Equals(redirectUri, codeInfo.RedirectUri, StringComparison.Ordinal))
        {
            return OAuthError("invalid_grant", "Redirect URI mismatch.");
        }

        if (string.IsNullOrEmpty(codeVerifier) || !VerifyCodeChallenge(codeVerifier, codeInfo.CodeChallenge))
        {
            return OAuthError("invalid_grant", "Code verifier does not match the challenge.");
        }

        if (!TryNormalizeResource(string.IsNullOrEmpty(resource) ? codeInfo.Resource : resource, options, out var audience))
        {
            return OAuthError("invalid_target", "The specified resource is not valid.");
        }

        // #region agent log
        AgentDebugLog.Write("C", "OAuth:IssueToken", "Token issued", new
        {
            clientId = client.ClientId,
            grantType,
            audience,
            scope = codeInfo.Scope,
            hasCode = !string.IsNullOrEmpty(code),
            hasVerifier = !string.IsNullOrEmpty(codeVerifier)
        });
        // #endregion
        var (token, expiresIn) = issuer.Issue(client.ClientId, audience, codeInfo.Scope);
        return Results.Json(new TokenResponse
        {
            AccessToken = token,
            ExpiresIn = expiresIn,
            Scope = codeInfo.Scope
        });
    }

    private static IResult OAuthError(string error, string description) =>
        Results.Json(new OAuthErrorResponse { Error = error, ErrorDescription = description }, statusCode: StatusCodes.Status400BadRequest);

    private static IResult RedirectError(string redirectUri, string error, string description, string? state)
    {
        var callback = QueryHelpers.AddQueryString(redirectUri, new Dictionary<string, string?>
        {
            ["error"] = error,
            ["error_description"] = description,
            ["state"] = state
        });
        return Results.Redirect(callback);
    }

    public static bool TryNormalizeResource(string? resource, SpikeAuthOptions options, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(resource))
        {
            normalized = options.Resource;
            return true;
        }

        var candidate = resource.TrimEnd('/');
        var allowed = new[]
        {
            options.Issuer,
            options.Resource.TrimEnd('/')
        };

        if (allowed.Any(value => string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase)))
        {
            normalized = options.Resource;
            return true;
        }

        normalized = options.Resource;
        return false;
    }

    public static bool VerifyCodeChallenge(string codeVerifier, string codeChallenge)
    {
        var computed = WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(codeVerifier)));
        var computedBytes = Encoding.UTF8.GetBytes(computed);
        var challengeBytes = Encoding.UTF8.GetBytes(codeChallenge);
        return computedBytes.Length == challengeBytes.Length &&
               CryptographicOperations.FixedTimeEquals(computedBytes, challengeBytes);
    }
}
