using System.Collections.Concurrent;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;

namespace CursorMcp.Server.Auth;

public sealed class OAuthStore
{
    private readonly ConcurrentDictionary<string, OAuthClient> _clients = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, LoginTicket> _tickets = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, AuthorizationCode> _codes = new(StringComparer.Ordinal);
    private readonly SpikeAuthOptions _options;

    public OAuthStore(IOptions<SpikeAuthOptions> options)
    {
        _options = options.Value;
        _clients[_options.DemoClientId] = new OAuthClient
        {
            ClientId = _options.DemoClientId,
            RequiresSecret = false,
            RedirectUris = []
        };
    }

    public OAuthClient GetOrCreateClient(string clientId)
    {
        return _clients.GetOrAdd(clientId, id => new OAuthClient
        {
            ClientId = id,
            RequiresSecret = false,
            RedirectUris = []
        });
    }

    public OAuthClient Register(IReadOnlyList<string> redirectUris, bool requiresSecret)
    {
        var client = new OAuthClient
        {
            ClientId = $"dyn-{Guid.NewGuid():N}",
            RequiresSecret = requiresSecret,
            RedirectUris = [.. redirectUris]
        };
        _clients[client.ClientId] = client;
        return client;
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
}
