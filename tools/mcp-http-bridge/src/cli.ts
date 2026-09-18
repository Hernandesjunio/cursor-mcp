import { resolve } from "node:path";
import { loadConfig, toMcpRemoteArgv } from "./config";

function usage(): never {
  console.error(
    "Usage: mcp-http-bridge --config <file.json>\n" +
      "   or: MCP_BRIDGE_CONFIG=<file.json> mcp-http-bridge",
  );
  process.exit(1);
}

function getConfigPath(): string {
  const args = process.argv.slice(2);
  const flagIndex = args.indexOf("--config");
  if (flagIndex >= 0) {
    const value = args[flagIndex + 1];
    if (!value || value.startsWith("-")) {
      usage();
    }
    return resolve(value);
  }

  const fromEnv = process.env.MCP_BRIDGE_CONFIG;
  if (fromEnv && fromEnv.trim() !== "") {
    return resolve(fromEnv);
  }

  usage();
}

function main(): void {
  let config;
  try {
    config = loadConfig(getConfigPath());
  } catch (error) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(message);
    process.exit(1);
  }

  process.argv = [process.argv[0], process.argv[1], ...toMcpRemoteArgv(config)];
  require("mcp-remote/dist/proxy.js");
}

main();
