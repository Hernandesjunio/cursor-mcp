# AGENTS

Spike .NET 10: MCP Streamable HTTP stateless em `https://localhost:7071/mcp`, OAuth local (cliente pré-registrado, **sem DCR**). Detalhes: [docs/cursor-conectividade.md](docs/cursor-conectividade.md).

OAuth `client_id` = Application ID B2C (`8f3a2c1b-6e4d-4a90-9c7e-1b2d3e4f5a60`). Nome do server no Cursor (`mcpServers.cursor-mcp-spike`) **não** é o `client_id`.

## Não faça

- `"url": "https://localhost:7071/mcp"` no Cursor com cert autoassinado (`fetch failed` / `DEPTH_ZERO_SELF_SIGNED_CERT`).
- `npx mcp-remote https://…` (npm 6 trata a URL como pacote → **-32000**).
- Bloco `auth` / `CLIENT_ID` na entrada **stdio** (`command` + `mcp-remote`). Cursor só honra `auth` em `"url"`.
- Assumir o `node` do PATH: o MCP do Cursor neste PC usa `C:\Program Files\nodejs` **v12**.

## HTTPS local (o que funciona)

1. Node **≥ 18** em path absoluto: `C:/Users/herna/AppData/Local/nodejs-lts/node.exe` (`-v` → v22.x).
2. Com esse Node: `npm install` (`mcp-remote`). Bin: `node_modules/mcp-remote/dist/proxy.js`.
3. PEM: `dotnet dev-certs https -ep certs/localhost.pem --format Pem --no-password`.
4. `.cursor/mcp.json`: `command` = node v22; `args` = `[proxy.js, https://localhost:7071/mcp, 9999, --host, 127.0.0.1, --static-oauth-client-info, @.cursor/oauth-client-info.json]`; `env.NODE_EXTRA_CA_CERTS` = `certs/localhost.pem`.
5. `SpikeAuth:Clients`: `ClientId=8f3a2c1b-6e4d-4a90-9c7e-1b2d3e4f5a60` (mesmo Guid em `oauth-client-info.json`). Redirects: `https://www.cursor.com/agents/mcp/oauth/callback`, `http://localhost:8787/callback` (Cursor nativo), `http://127.0.0.1:9999/oauth/callback` (mcp-remote). Loopback ignora porta.
6. Se existir `~/.cursor/mcp.json` com o mesmo nome, o do **projeto é ignorado**. Alinhar ou apagar o user-level. Cache DCR: `~/.mcp-auth/mcp-remote-v1/`.
7. Recarregar a **janela** (não só o MCP). Authenticate → `/login` → `demo-user` → `hello_world`.

## HTTP loopback (mais simples)

`PublicBaseUrl` + Kestrel `http://localhost:7071`. mcp.json nativo:

```json
{
  "mcpServers": {
    "cursor-mcp-spike": {
      "url": "http://localhost:7071/mcp",
      "auth": {
        "CLIENT_ID": "8f3a2c1b-6e4d-4a90-9c7e-1b2d3e4f5a60",
        "scopes": ["mcp:tools"]
      }
    }
  }
}
```

Sem proxy/PEM. Sem `CLIENT_SECRET` (cliente público + PKCE).

## Produção

CA pública: `"url": "https://mcp.dominio/mcp"` + o mesmo `auth.CLIENT_ID`. Sem `mcp-remote`.

## Sintomas

| Erro | Causa |
| --- | --- |
| 405 em `GET /mcp` | OAuth não foi a `/authorize` (`client_id` / `redirect_uri`) |
| -32000 | npx/Node 12 instalando a URL |
| `fetch failed` | TLS do host Cursor — não usar `"url"` HTTPS local |
| `dyn-*` | falta `--static-oauth-client-info` (stdio) ou `auth.CLIENT_ID` (`url`) |
