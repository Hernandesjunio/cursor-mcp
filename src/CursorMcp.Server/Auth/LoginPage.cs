using System.Net;

namespace CursorMcp.Server.Auth;

internal static class LoginPage
{
    public static string Render(string ticket, string clientId, string clientName)
    {
        var safeTicket = WebUtility.HtmlEncode(ticket);
        var safeClient = WebUtility.HtmlEncode(clientId);
        var safeName = WebUtility.HtmlEncode(clientName);

        return $$"""
            <!DOCTYPE html>
            <html lang="pt-BR">
            <head>
              <meta charset="utf-8" />
              <meta name="viewport" content="width=device-width, initial-scale=1" />
              <title>Login — Cursor MCP Spike</title>
              <style>
                :root { color-scheme: dark; }
                body {
                  font-family: Segoe UI, sans-serif;
                  margin: 0;
                  min-height: 100vh;
                  display: grid;
                  place-items: center;
                  background: #0f1115;
                  color: #f4f4f5;
                }
                main {
                  width: min(28rem, calc(100vw - 2rem));
                  background: #181b21;
                  border: 1px solid #2a2f3a;
                  border-radius: 16px;
                  padding: 2rem;
                  box-shadow: 0 20px 50px rgba(0, 0, 0, 0.35);
                }
                h1 { margin: 0 0 0.5rem; font-size: 1.4rem; }
                p { color: #c3c7d1; line-height: 1.5; }
                button {
                  width: 100%;
                  margin-top: 1.25rem;
                  border: 0;
                  border-radius: 10px;
                  padding: 0.85rem 1rem;
                  background: #4f8cff;
                  color: white;
                  font-size: 1rem;
                  font-weight: 600;
                  cursor: pointer;
                }
                button:hover { background: #3b78ea; }
                code { color: #9cdcfe; }
              </style>
            </head>
            <body>
              <main>
                <h1>Autorizar {{safeName}}</h1>
                <p>Simulação local de aplicação pré-registrada (Azure B2C). Cliente: <code>{{safeClient}}</code>.</p>
                <p>Ao continuar, um authorization code é gerado e o callback do cliente é executado.</p>
                <form method="post" action="/login">
                  <input type="hidden" name="ticket" value="{{safeTicket}}" />
                  <button type="submit">Entrar como demo-user</button>
                </form>
              </main>
            </body>
            </html>
            """;
    }
}
