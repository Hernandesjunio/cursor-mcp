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
}
