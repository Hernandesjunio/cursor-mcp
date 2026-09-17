# Cursor MCP Spike

Servidor MCP HTTP **stateless** (`2026-07-28`) em **.NET 10**, servido em `https://localhost:7071`, com login local, challenge `WWW-Authenticate` e JWT.

Este repositório é um spike de conectividade Cursor ↔ MCP. Credenciais, chave HMAC e armazenamento em memória **não são adequados para produção**.

Como conectar o Cursor (HTTP loopback vs HTTPS autoassinado, Node 22, PEM, `mcp-remote`): [docs/cursor-conectividade.md](docs/cursor-conectividade.md). Instruções curtas para IA: [AGENTS.md](AGENTS.md).

## O que está incluso

- `POST /mcp` — Streamable HTTP MCP, sem sessão (`HttpServerSessionMode.Stateless`)
- Tool `hello_world`
- Resource `hello://world`
- `401` + `WWW-Authenticate: Bearer resource_metadata="https://localhost:7071/.well-known/oauth-protected-resource"`
- Authorization Server local que simula um cadastro Azure B2C: clientes pré-registrados em `SpikeAuth:Clients` (`/authorize`, `/login`, `/token`)
- Scalar em `/scalar` para os endpoints HTTP auxiliares (OAuth/health). O endpoint MCP **não** é executável pelo Scalar.

## Pré-requisitos

- SDK .NET 10 (`10.0.301` ou compatível; ver `global.json`)
- Certificado de desenvolvimento HTTPS confiável:

```bash
dotnet dev-certs https --trust
```

## Como rodar

```bash
dotnet run --project src/CursorMcp.Server --launch-profile https
```

URLs:

| Recurso | URL |
| --- | --- |
| Home | https://localhost:7071/ |
| Scalar | https://localhost:7071/scalar |
| Health | https://localhost:7071/health |
| MCP | https://localhost:7071/mcp |
| Protected resource metadata | https://localhost:7071/.well-known/oauth-protected-resource |
| Authorization server metadata | https://localhost:7071/.well-known/oauth-authorization-server |
| Login | https://localhost:7071/login |

## Conectar no Cursor

HTTP em loopback é o caminho simples. `"url": "https://localhost:7071/mcp"` **não** funciona no Cursor só com `dotnet dev-certs https --trust` (o fetch do MCP ignora o store do Windows).

Playbook completo (HTTP vs HTTPS local, Node 22, PEM, `proxy.js`, OAuth com cliente pré-registrado): [docs/cursor-conectividade.md](docs/cursor-conectividade.md).

Loopback HTTP:

```json
{
  "mcpServers": {
    "cursor-mcp-spike": {
      "url": "http://localhost:7071/mcp"
    }
  }
}
```

HTTPS local autoassinado: Node 22 + `mcp-remote` (`dist/proxy.js`) + `NODE_EXTRA_CA_CERTS` — ver o doc. Produção com CA pública: `"url": "https://mcp.dominio/mcp"`.

Depois de alterar o `mcp.json`, recarregue a **janela** do Cursor. Authenticate → `/login` → **Entrar como demo-user**. Confirme `hello_world` e `hello://world`.

Chamadas JSON-RPC manuais para `2026-07-28` precisam dos headers `MCP-Protocol-Version` e `Mcp-Method` (e `Mcp-Name` em `tools/call` / `resources/read`). O Cursor envia isso automaticamente.

O `401` do MCP só anuncia `resource_metadata`. O `client_id` chega no `GET /authorize` e precisa existir em `SpikeAuth:Clients`, com `redirect_uri` cadastrado para essa aplicação (loopback ignora a porta, como apps nativas no Entra/B2C). Não há Dynamic Client Registration: este spike simula um app B2C já registrado, não um AS que emite `client_id` sob demanda.

## Fluxo OAuth

```
Cursor → POST /mcp (sem Bearer)
Server → 401 WWW-Authenticate resource_metadata=...
Cursor → GET /.well-known/oauth-protected-resource
Cursor → GET /.well-known/oauth-authorization-server
Cursor → GET /authorize?client_id=cursor-mcp-spike&response_type=code&code_challenge=...&redirect_uri=...
Server → 302 /login?ticket=...
User   → POST /login (botão)
Server → 302 callback?code=...&state=...
Cursor → POST /token (code + code_verifier)
Server → access_token JWT (15 min) + refresh_token
Cursor → POST /mcp Authorization: Bearer ...
Cursor → POST /token (grant_type=refresh_token) quando o access expira
Server → novo access_token + refresh_token rotacionado
```

## Contratos

- `POST /mcp` exige JWT HS256 com `iss=https://localhost:7071`, `aud=https://localhost:7071/mcp` (ou o origin) e `scope=mcp:tools`.
- `GET /authorize` exige `client_id` pré-registrado, `redirect_uri` da aplicação, `response_type=code` e PKCE `S256`.
- `POST /login` consome o ticket de uso único e redireciona ao `redirect_uri` do cliente.
- `POST /token` valida `client_id` pré-registrado, code de uso único, expiração, redirect URI e PKCE `S256`, e devolve JWT + `refresh_token`.
- `grant_type=refresh_token` emite um novo access token e rotaciona o refresh (reuse do antigo → `invalid_grant` e revoga a família).
- JWT de demonstração dura 15 minutos. Refresh token dura 7 dias. Authorization code dura 5 minutos.

## Smoke tests

```bash
bash scripts/smoke.sh
```

O script sobe o servidor se `/health` não responder e valida health, Scalar, discovery, clientes pré-registrados, challenge 401, fluxo PKCE/login/token, refresh token com rotação, chamadas MCP autenticadas e os casos negativos (cliente desconhecido, redirect URI inválida, code reutilizado, PKCE inválido, JWT expirado/inválido).

## Checklist de validação

- [ ] `dotnet dev-certs https --trust` executado
- [ ] `dotnet build -c Release` sem erros
- [ ] App escuta apenas `https://localhost:7071`
- [ ] `/health` retorna `{ "status": "ok" }`
- [ ] `/scalar` abre e lista health/OAuth
- [ ] `/.well-known/oauth-protected-resource` contém `resource` e `authorization_servers`
- [ ] `/.well-known/oauth-authorization-server` contém `authorization_endpoint`, `token_endpoint`, `grant_types_supported` com `refresh_token` e `code_challenge_methods_supported: ["S256"]`, sem `registration_endpoint`
- [ ] `POST /mcp` sem token → `401`
- [ ] Header `WWW-Authenticate` contém `Bearer` e `resource_metadata="https://localhost:7071/.well-known/oauth-protected-resource"`
- [ ] `client_id` desconhecido em `/authorize` → `unauthorized_client`
- [ ] `redirect_uri` não cadastrado → `invalid_request`
- [ ] `/authorize` com cliente registrado redireciona para `/login`
- [ ] Botão de login gera callback com `code` e `state`
- [ ] `/token` devolve JWT Bearer e `refresh_token`
- [ ] JWT contém `sub=demo-user` e `scope=mcp:tools`
- [ ] `tools/list` mostra `hello_world`
- [ ] `tools/call` de `hello_world` retorna `Hello, world!`
- [ ] `resources/list` mostra `hello://world`
- [ ] `resources/read` de `hello://world` retorna `Hello, world!`
- [ ] Code OAuth reutilizado → `invalid_grant`
- [ ] `code_verifier` errado → `invalid_grant`
- [ ] Refresh token válido emite novo access e rotaciona o refresh
- [ ] Refresh token reutilizado → `invalid_grant`
- [ ] JWT expirado ou inválido → `401`
- [ ] Cursor completa o login e passa a listar a tool/resource (playbook: [docs/cursor-conectividade.md](docs/cursor-conectividade.md))

## Limitações do spike

- Sem banco, consentimento real ou rotação de chaves
- HMAC compartilhado em `appsettings.json`
- Clientes e redirect URIs vêm de configuração em memória; não há Dynamic Client Registration
- Sem suíte de testes de unidade; a garantia é o smoke HTTP/MCP
