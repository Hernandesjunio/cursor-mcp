# Conectividade Cursor ↔ MCP

Dois jeitos: `"url"` (fetch do Cursor) ou `command` + stdio (bridge em `tools/mcp-http-bridge/dist/mcp-http-bridge.js`). O stdio **não** encapsula o server .NET.

| Situação | `mcp.json` | `client_id` |
| --- | --- | --- |
| HTTP loopback | `"url": "http://localhost:7071/mcp"` | `auth.CLIENT_ID` |
| Produção / CA pública | `"url": "https://mcp.dominio/mcp"` | `auth.CLIENT_ID` |
| `localhost` autoassinado ou CA privada | `command` + bridge | `.cursor/mcp-bridge.json` |

Não use `"url": "https://localhost:…"`: o fetch do Cursor ignora o store do Windows (`fetch failed` / `DEPTH_ZERO_SELF_SIGNED_CERT`). Depois de mudar `mcp.json`, **Developer: Reload Window**. Se existir `%USERPROFILE%\.cursor\mcp.json` com o server `cursor-mcp-spike`, o do **projeto é ignorado**.

Redirects no AS: `https://www.cursor.com/agents/mcp/oauth/callback` e `http://localhost:8787/callback`. `client_id` = `8f3a2c1b-6e4d-4a90-9c7e-1b2d3e4f5a60` (não é o nome `cursor-mcp-spike`). Sem DCR.

---

## HTTP (simples)

1. Kestrel e `SpikeAuth.PublicBaseUrl` em `http://localhost:7071`.
2. `.cursor/mcp.json`:

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

3. `dotnet run --project src/CursorMcp.Server --launch-profile https` (vale a URL no `appsettings.json`).
4. Reload Window → Authenticate → **Entrar como demo-user** → `hello_world`.

Sem bridge, PEM ou Node. Sem `CLIENT_SECRET`. Callback: `http://localhost:8787/callback`.

---

## Produção (sem proxy)

CA **já** na Mozilla CA do host Cursor. Sem bridge.

```json
{
  "mcpServers": {
    "cursor-mcp-spike": {
      "url": "https://mcp.seudominio.com/mcp",
      "auth": {
        "CLIENT_ID": "8f3a2c1b-6e4d-4a90-9c7e-1b2d3e4f5a60",
        "scopes": ["mcp:tools"]
      }
    }
  }
}
```

Não misture `"url"` com `command` na mesma entrada. Cert “corporativo” só no Windows e o Cursor recusa → seção seguinte.

---

## TLS não confiável (localhost e internos)

O Cursor fala MCP por stdin/stdout; o **Node** faz o HTTPS (honra `NODE_EXTRA_CA_CERTS`). Nunca `npx mcp-remote`. Nunca `"command": "node"` (PATH do MCP pode ser Node **12**).

O JS em `dist/` **já contém** mcp-remote (~4 MB). Linhas `// ../../node_modules/...` no arquivo são rótulos do esbuild, não `require` de pasta. Rodar o MCP **não** precisa de `node_modules`. `npm run build:bridge` só para rebuild.

### Pré-requisito

Programa **Node.js** instalado, versão **18, 20 ou 22**. A variável **`NODE22_ABSOLUTE_PATH`** (nome exato) aponta para o arquivo **`node.exe`** — caminho completo, não a pasta. O “22” no nome é o pin deste spike; o valor pode ser v18/v20/v22.

### 1. Achar o `node.exe` certo

1. Tecla Windows, digite `cmd`, abra **Prompt de Comando**.
2. Digite `where.exe node` e Enter. Cada linha é um `node.exe`.
3. Para **cada** linha, teste a versão (aspas se o caminho tiver espaço):

```bat
"C:\Users\SEU_USUARIO\AppData\Local\nodejs-lts\node.exe" -v
```

4. Serve `v18...`, `v20...` ou `v22...`. **Não serve** `v12...`.
5. Se nenhum ≥ 18: instale o LTS em [https://nodejs.org](https://nodejs.org) (marque “Add to PATH” se aparecer) e rode `where.exe node` de novo.
6. Copie o caminho **inteiro**, incluindo `node.exe`.

### 2. Criar `NODE22_ABSOLUTE_PATH` (neste PC, fora do Git)

1. Tecla Windows → `variaveis de ambiente` → **Editar as variáveis de ambiente do sistema** → **Variáveis de Ambiente**.
2. Em **Variáveis do usuário** (caixa de cima — não “do sistema”) → **Novo**.
3. Nome: `NODE22_ABSOLUTE_PATH` (exato, maiúsculas, underscores).
4. Valor: o caminho do passo 1 (`node.exe`, **sem** aspas).
5. OK em todas as janelas.
6. **Feche o Cursor por completo** (Arquivo → Sair) e abra de novo. Recarregar a janela **não** basta.

### 3. Conferir a variável

Abra um Prompt **novo** (feche um antigo):

```bat
echo %NODE22_ABSOLUTE_PATH%
"%NODE22_ABSOLUTE_PATH%" -v
```

Tem que aparecer o path e uma versão ≥ 18. Se `echo` mostrar `%NODE22_ABSOLUTE_PATH%` ou vazio: feche o Prompt, abra outro; se continuar vazio, volte ao passo 2.

### 4. PEM (HTTPS local)

```bat
mkdir certs
dotnet dev-certs https -ep certs/localhost.pem --format Pem --no-password
```

`certs/` está no `.gitignore`. Host interno: exporte a CA/leaf para um PEM. Vários certs = vários `BEGIN CERTIFICATE` no **mesmo** arquivo. `dotnet dev-certs https --trust` **não** basta para o Node.

### 5. `mcp.json` do projeto (sem pasta `C:\Users\...`)

```json
{
  "mcpServers": {
    "cursor-mcp-spike": {
      "command": "${env:NODE22_ABSOLUTE_PATH}",
      "args": [
        "${workspaceFolder}/tools/mcp-http-bridge/dist/mcp-http-bridge.js",
        "--config",
        "${workspaceFolder}/.cursor/mcp-bridge.json"
      ],
      "env": {
        "NODE_EXTRA_CA_CERTS": "${workspaceFolder}/certs/localhost.pem"
      }
    }
  }
}
```

`.cursor/mcp-bridge.json`: `callbackPort` **8787**, `callbackPath` **`/callback`**, `host` **`localhost`**, `client_id` no Guid acima. Sem `auth.CLIENT_ID` nesta entrada stdio.

Se existir `C:\Users\SEU_USUARIO\.cursor\mcp.json` com `cursor-mcp-spike`, apague essa entrada ou deixe igual. Senão você edita o arquivo do projeto e nada muda.

### 6. Subir, recarregar, autenticar

```bat
dotnet run --project src/CursorMcp.Server --launch-profile https
```

Command Palette → `Developer: Reload Window` → Authenticate → `/login` (não `/mcp`) → `demo-user` → `hello_world`.

Callback: `http://localhost:8787/callback` (igual ao Cursor `"url"` e ao B2C). `9999` + `/oauth/callback` só vale se o AS tiver essa URI (SpikeAuth local). B2C com só 8787 rejeita o redirect antigo. Porta 8787 ocupada = outro Cursor/`url` já escutando.

---

## OAuth (well-known)

Well-known **não** traz `redirect_uri` nem token. Fluxo: `401` + resource metadata → AS metadata (`authorization_endpoint`, `token_endpoint`) → o client **monta** `/authorize` com `client_id` + `redirect_uri` da config + PKCE → POST `/token`.

| Modo | `client_id` |
| --- | --- |
| `"url"` | `auth.CLIENT_ID` |
| stdio | `staticOAuthClientInfo` no JSON do bridge |

---

## Sintomas

| Erro | Causa |
| --- | --- |
| 405 em `GET /mcp` | `client_id` / `redirect_uri` |
| -32000 | npx / Node 12 |
| `fetch failed` | `"url"` HTTPS com cert que o Cursor não confia |
| `dyn-*` | falta Guid no JSON (stdio) ou `auth.CLIENT_ID` (`url`) |
| command vazio | `NODE22_ABSOLUTE_PATH` ausente ou Cursor não foi fechado |
| bind 8787 | outro processo no callback |
