using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace CursorMcp.Server.Auth;

public sealed class JwtTokenIssuer
{
    private readonly SpikeAuthOptions _options;
    private readonly SigningCredentials _credentials;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenIssuer(IOptions<SpikeAuthOptions> options)
    {
        _options = options.Value;
        _credentials = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey)),
            SecurityAlgorithms.HmacSha256);
    }

    public (string Token, int ExpiresIn) Issue(
        string clientId,
        string audience,
        string scope,
        bool expired = false)
    {
        var now = DateTime.UtcNow;
        var expires = expired ? now.AddMinutes(-5) : now.AddMinutes(_options.AccessTokenMinutes);
        var issuedAt = expired ? now.AddMinutes(-15) : now;

        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = audience,
            Subject = new ClaimsIdentity(
            [
                new Claim("sub", _options.DemoUser),
                new Claim("name", "Demo User"),
                new Claim("client_id", clientId),
                new Claim("scope", scope)
            ]),
            NotBefore = issuedAt,
            IssuedAt = issuedAt,
            Expires = expires,
            SigningCredentials = _credentials
        };

        return (_handler.CreateToken(descriptor), _options.AccessTokenMinutes * 60);
    }
}
