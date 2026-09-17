using System.Text;
using CursorMcp.Server;
using CursorMcp.Server.Auth;
using CursorMcp.Server.Resources;
using CursorMcp.Server.Tools;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.AspNetCore.Authentication;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<SpikeAuthOptions>(builder.Configuration.GetSection(SpikeAuthOptions.SectionName));
var spikeAuth = builder.Configuration.GetSection(SpikeAuthOptions.SectionName).Get<SpikeAuthOptions>() ?? new SpikeAuthOptions();
var signingKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(spikeAuth.SigningKey));

builder.Services.AddSingleton<OAuthStore>();
builder.Services.AddSingleton<JwtTokenIssuer>();

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = spikeAuth.Issuer.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    options.MapInboundClaims = false;
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            // #region agent log
            AgentDebugLog.Write("C", "Program.cs:JwtFailed", "JWT authentication failed", new
            {
                errorType = context.Exception.GetType().Name,
                error = context.Exception.Message
            });
            // #endregion
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            // #region agent log
            AgentDebugLog.Write("C", "Program.cs:JwtChallenge", "JWT challenge issued", new
            {
                path = context.Request.Path.Value,
                hasAuthorization = context.Request.Headers.ContainsKey("Authorization"),
                error = context.Error,
                errorDescription = context.ErrorDescription
            });
            // #endregion
            return Task.CompletedTask;
        }
    };
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = spikeAuth.Issuer,
        ValidAudiences = [spikeAuth.Resource, spikeAuth.Issuer, $"{spikeAuth.Resource}/", $"{spikeAuth.Issuer}/"],
        IssuerSigningKey = signingKey,
        ClockSkew = TimeSpan.Zero,
        NameClaimType = "name",
        AudienceValidator = (audiences, _, _) =>
        {
            if (audiences is null)
            {
                return false;
            }

            foreach (var audience in audiences)
            {
                var normalized = audience.TrimEnd('/');
                if (string.Equals(normalized, spikeAuth.Resource.TrimEnd('/'), StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(normalized, spikeAuth.Issuer, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
    };
})
.AddMcp(options =>
{
    options.ResourceMetadataUri = new Uri($"{spikeAuth.Issuer}/.well-known/oauth-protected-resource");
    options.ResourceMetadata = new()
    {
        Resource = spikeAuth.Resource,
        ResourceName = "Cursor MCP Spike",
        ResourceDocumentation = $"{spikeAuth.Issuer}/scalar",
        AuthorizationServers = { spikeAuth.Issuer },
        ScopesSupported = [spikeAuth.Scope],
        BearerMethodsSupported = ["header"]
    };
});

builder.Services.AddAuthorization();
builder.Services.AddOpenApi(options =>
{
    options.AddDocumentTransformer((document, _, _) =>
    {
        document.Info.Title = "Cursor MCP Spike";
        document.Info.Version = "1.0.0";
        document.Info.Description =
            "Endpoints auxiliares de OAuth, health e diagnóstico. O MCP Streamable HTTP autenticado fica em POST /mcp e não é executável pelo Scalar.";
        return Task.CompletedTask;
    });
});

builder.Services.AddMcpServer()
    .WithHttpTransport(options => options.SessionMode = HttpServerSessionMode.Stateless)
    .WithTools<HelloWorldTools>()
    .WithResources<HelloWorldResources>();

builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Information);
builder.Logging.AddFilter("Microsoft.AspNetCore.Server.Kestrel", LogLevel.Information);

var app = builder.Build();

app.Use(async (context, next) =>
{
    var path = context.Request.Path.Value ?? "";
    var started = DateTimeOffset.UtcNow;
    // #region agent log
    AgentDebugLog.Write("F", "Program.cs:RequestStart", "HTTP request started", new
    {
        method = context.Request.Method,
        path,
        protocol = context.Request.Protocol,
        scheme = context.Request.Scheme,
        contentType = context.Request.ContentType,
        accept = context.Request.Headers.Accept.ToString(),
        userAgent = context.Request.Headers.UserAgent.ToString(),
        hasAuthorization = context.Request.Headers.ContainsKey("Authorization"),
        mcpMethod = context.Request.Headers["Mcp-Method"].ToString(),
        mcpProtocol = context.Request.Headers["MCP-Protocol-Version"].ToString(),
        connectionId = context.Connection.Id
    });
    // #endregion
    await next();
    // #region agent log
    AgentDebugLog.Write("B", "Program.cs:Request", "HTTP request completed", new
    {
        method = context.Request.Method,
        path,
        status = context.Response.StatusCode,
        protocol = context.Request.Protocol,
        hasAuthorization = context.Request.Headers.ContainsKey("Authorization"),
        mcpMethod = context.Request.Headers["Mcp-Method"].ToString(),
        mcpProtocol = context.Request.Headers["MCP-Protocol-Version"].ToString(),
        durationMs = (DateTimeOffset.UtcNow - started).TotalMilliseconds,
        connectionId = context.Connection.Id
    });
    // #endregion
});

app.UseAuthentication();
app.UseAuthorization();

app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.WithTitle("Cursor MCP Spike");
});

app.MapGet("/", () => Results.Content("""
    <!DOCTYPE html>
    <html lang="pt-BR">
    <head><meta charset="utf-8" /><title>Cursor MCP Spike</title></head>
    <body style="font-family: Segoe UI, sans-serif; max-width: 40rem; margin: 3rem auto;">
      <h1>Cursor MCP Spike</h1>
      <p>Servidor MCP HTTPS stateless 2026-07-28 com login local e JWT.</p>
      <ul>
        <li><a href="/scalar">Scalar</a></li>
        <li><a href="/health">Health</a></li>
        <li><a href="/.well-known/oauth-protected-resource">Protected resource metadata</a></li>
        <li><a href="/.well-known/oauth-authorization-server">Authorization server metadata</a></li>
      </ul>
      <p>Endpoint MCP protegido: <code>POST /mcp</code></p>
    </body>
    </html>
    """, "text/html; charset=utf-8"))
    .ExcludeFromDescription();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    protocol = "2026-07-28",
    transport = "streamable-http-stateless",
    mcp = spikeAuth.Resource
}))
    .WithTags("Health")
    .WithSummary("Liveness do spike MCP.");

if (app.Environment.IsDevelopment())
{
    app.MapGet("/dev/expired-token", (JwtTokenIssuer issuer, Microsoft.Extensions.Options.IOptions<SpikeAuthOptions> options) =>
    {
        var auth = options.Value;
        var (token, _) = issuer.Issue(auth.DemoClientId, auth.Resource, auth.Scope, expired: true);
        return Results.Ok(new { access_token = token, token_type = "Bearer" });
    })
    .WithTags("Dev")
    .WithSummary("Emite um JWT já expirado para smoke tests de rejeição.");
}

app.MapSpikeOAuth();
app.MapMcp("/mcp").RequireAuthorization();

// #region agent log
AgentDebugLog.Write("A", "Program.cs:Startup", "MCP spike starting", new
{
    issuer = spikeAuth.Issuer,
    resource = spikeAuth.Resource,
    scope = spikeAuth.Scope,
    demoClientId = spikeAuth.DemoClientId
});
// #endregion
app.Logger.LogInformation("MCP spike listening at {Url}", spikeAuth.Issuer);
app.Logger.LogInformation("Protected resource: {Resource}", spikeAuth.Resource);
app.Logger.LogInformation("Scalar UI: {Scalar}", $"{spikeAuth.Issuer}/scalar");

app.Run();
