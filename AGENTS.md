# AGENTS

Spike .NET 10: MCP Streamable HTTP stateless em `https://localhost:7071/mcp`, OAuth local (cliente pré-registrado, **sem DCR**). Passo a passo: [docs/cursor-conectividade.md](docs/cursor-conectividade.md).

OAuth `client_id` = Application ID B2C (`8f3a2c1b-6e4d-4a90-9c7e-1b2d3e4f5a60`). Nome do server no Cursor (`mcpServers.cursor-mcp-spike`) **não** é o `client_id`.

O stdio **não** encapsula o server .NET: é um bridge (`tools/mcp-http-bridge/dist/mcp-http-bridge.js`) que fala MCP por stdin/stdout com o Cursor e HTTP com o MCP remoto. O `dist` já inclui mcp-remote; **não** aponta para `node_modules`.

## Não faça

- `"url": "https://…"` no Cursor com cert autoassinado, CA privada ou host interno não confiável (`fetch failed` / `DEPTH_ZERO_SELF_SIGNED_CERT`).
- `npx mcp-remote https://…` (npm 6 trata a URL como pacote → **-32000**).
- `"command": "node"` — o PATH do MCP do Cursor neste Windows resolve Node **12**.
- Bloco `auth` / `CLIENT_ID` na entrada **stdio**. Cursor só honra `auth` em `"url"`.
- Path de usuário no `mcp.json` versionado (`C:/Users/…`). Usar `${env:NODE22_ABSOLUTE_PATH}` + `${workspaceFolder}`.

## TLS não confiável (localhost / interno)

1. Variável de usuário Windows **`NODE22_ABSOLUTE_PATH`** = caminho completo do `node.exe` ≥ 18 (como achar: [docs/cursor-conectividade.md](docs/cursor-conectividade.md)). Fechar o Cursor por completo depois de criar a variável.
2. `.cursor/mcp.json` do projeto: `command` = `${env:NODE22_ABSOLUTE_PATH}`; `args` = bundle + `--config` + `.cursor/mcp-bridge.json`; `env.NODE_EXTRA_CA_CERTS` = PEM via `${workspaceFolder}`.
3. Recarregar a **janela**. Se existir `~/.cursor/mcp.json` com o mesmo server name, o do projeto é **ignorado** — alinhar ou apagar. Authenticate → `/login` → `demo-user` → `hello_world`.

`.cursor/mcp-bridge.json`: `callbackPort` **8787**, `callbackPath` **`/callback`**, `host` **`localhost`** → `http://localhost:8787/callback` (Cursor `"url"` e B2C). SpikeAuth ainda aceita `http://127.0.0.1:9999/oauth/callback`; B2C corporativo **não**.

Well-known só publica endpoints. O client monta `/authorize` com `client_id` + `redirect_uri` da **config**.

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

Sem proxy/PEM. Sem `CLIENT_SECRET`. Callback: `http://localhost:8787/callback`.

## Produção (sem proxy)

CA pública (Mozilla CA do host Cursor): `"url": "https://mcp.dominio/mcp"` + o mesmo `auth.CLIENT_ID`. Sem bridge. Host interno / CA privada = TLS não confiável.

## Sintomas

| Erro | Causa |
| --- | --- |
| 405 em `GET /mcp` | OAuth não foi a `/authorize` (`client_id` / `redirect_uri`) |
| -32000 | npx/Node 12 |
| `fetch failed` | TLS do host Cursor — não usar `"url"` HTTPS local/interno |
| `dyn-*` | falta `staticOAuthClientInfo` (stdio) ou `auth.CLIENT_ID` (`url`) |
| bind 8787 | outro processo já escuta o callback |
| command vazio | `NODE22_ABSOLUTE_PATH` ausente ou Cursor não foi fechado após criar a variável |
