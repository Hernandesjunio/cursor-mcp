using System.Collections.Concurrent;
using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CursorMcp.Server.Auth;

public sealed class OAuthStore
{
    private readonly ConcurrentDictionary<string, OAuthClient> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LoginTicket> _tickets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AuthorizationCode> _codes = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, RefreshToken> _refreshTokens = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _consumedRefreshTokens = new(StringComparer.Ordinal);
    private readonly SpikeAuthOptions _options;

    public OAuthStore(IOptions<SpikeAuthOptions> options)
    {
        _options = options.Value;
        foreach (var registered in _options.Clients)
        {
            if (string.IsNullOrWhiteSpace(registered.ClientId))
            {
                continue;
            }

            _clients[registered.ClientId] = new OAuthClient
            {
                ClientId = registered.ClientId,
                Name = string.IsNullOrWhiteSpace(registered.Name) ? registered.ClientId : registered.Name,
                ClientSecret = registered.ClientSecret,
                RequiresSecret = registered.RequiresSecret,
                RedirectUris = [.. registered.RedirectUris]
            };
        }
    }

    public bool TryGetClient(string clientId, out OAuthClient client) =>
        _clients.TryGetValue(clientId, out client!);

    public string CreateTicket(LoginTicket ticket)
    {
        _tickets[ticket.Ticket] = ticket;
        return ticket.Ticket;
    }

    public bool TryTakeTicket(string ticket, out LoginTicket value)
    {
        if (_tickets.TryRemove(ticket, out value!))
        {
            if (value.ExpiresAt >= DateTimeOffset.UtcNow)
            {
                return true;
            }
        }

        value = null!;
        return false;
    }

    public bool TryPeekTicket(string ticket, out LoginTicket value)
    {
        if (_tickets.TryGetValue(ticket, out value!) && value.ExpiresAt >= DateTimeOffset.UtcNow)
        {
            return true;
        }

        value = null!;
        return false;
    }

    public string CreateCode(LoginTicket ticket)
    {
        var code = WebEncoders.Base64UrlEncode(Guid.NewGuid().ToByteArray());
        _codes[code] = new AuthorizationCode
        {
            Code = code,
            ClientId = ticket.ClientId,
            RedirectUri = ticket.RedirectUri,
            CodeChallenge = ticket.CodeChallenge,
            Resource = ticket.Resource,
            Scope = ticket.Scope,
            ExpiresAt = DateTimeOffset.UtcNow.AddMinutes(_options.AuthorizationCodeMinutes)
        };
        return code;
    }

    public bool TryTakeCode(string code, out AuthorizationCode value)
    {
        if (_codes.TryRemove(code, out value!))
        {
            if (value.ExpiresAt >= DateTimeOffset.UtcNow)
            {
                return true;
            }
        }

        value = null!;
        return false;
    }

    public string CreateRefreshToken(string clientId, string audience, string scope, string? familyId = null)
    {
        var token = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        _refreshTokens[token] = new RefreshToken
        {
            Token = token,
            FamilyId = string.IsNullOrWhiteSpace(familyId) ? Guid.NewGuid().ToString("N") : familyId,
            ClientId = clientId,
            Audience = audience,
            Scope = scope,
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(_options.RefreshTokenDays)
        };
        return token;
    }

    public bool TryTakeRefreshToken(string token, out RefreshToken value)
    {
        if (_consumedRefreshTokens.TryGetValue(token, out var familyId))
        {
            RevokeFamily(familyId);
            value = null!;
            return false;
        }

        if (_refreshTokens.TryRemove(token, out value!))
        {
            _consumedRefreshTokens[token] = value.FamilyId;
            if (value.ExpiresAt >= DateTimeOffset.UtcNow)
            {
                return true;
            }
        }

        value = null!;
        return false;
    }

    private void RevokeFamily(string familyId)
    {
        foreach (var pair in _refreshTokens)
        {
            if (string.Equals(pair.Value.FamilyId, familyId, StringComparison.Ordinal))
            {
                _refreshTokens.TryRemove(pair.Key, out _);
            }
        }
    }
}
