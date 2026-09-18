import { readFileSync } from "node:fs";
import { dirname, isAbsolute, resolve } from "node:path";

export type OAuthClientInfo = {
  client_id: string;
  [key: string]: unknown;
};

export type BridgeConfig = {
  mcpUrl: string;
  callbackPort: number;
  callbackPath: string;
  host: string;
  staticOAuthClientInfo: OAuthClientInfo | string;
  allowHttp: boolean;
  debug: boolean;
};

const DEFAULTS = {
  callbackPort: 8787,
  callbackPath: "/callback",
  host: "localhost",
  allowHttp: false,
  debug: false,
} as const;

function fail(message: string): never {
  throw new Error(message);
}

function isHttpUrl(value: string): boolean {
  try {
    const url = new URL(value);
    return url.protocol === "http:" || url.protocol === "https:";
  } catch {
    return false;
  }
}

function asNonEmptyString(value: unknown, field: string): string {
  if (typeof value !== "string" || value.trim() === "") {
    fail(`${field} must be a non-empty string`);
  }
  return value.trim();
}

function parseClientInfo(value: unknown, configPath: string): OAuthClientInfo | string {
  if (typeof value === "string") {
    const trimmed = value.trim();
    if (trimmed === "") {
      fail("staticOAuthClientInfo must be a non-empty string or object");
    }
    if (trimmed.startsWith("@")) {
      const filePath = trimmed.slice(1);
      return `@${isAbsolute(filePath) ? filePath : resolve(dirname(configPath), filePath)}`;
    }
    return `@${isAbsolute(trimmed) ? trimmed : resolve(dirname(configPath), trimmed)}`;
  }

  if (value === null || typeof value !== "object" || Array.isArray(value)) {
    fail("staticOAuthClientInfo must be an object with client_id, or a path string");
  }

  const record = value as Record<string, unknown>;
  const clientId = asNonEmptyString(record.client_id, "staticOAuthClientInfo.client_id");
  return { ...record, client_id: clientId };
}

export function loadConfig(configPath: string): BridgeConfig {
  let rawText: string;
  try {
    rawText = readFileSync(configPath, "utf8");
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error);
    fail(`Cannot read config file ${configPath}: ${detail}`);
  }

  let raw: unknown;
  try {
    raw = JSON.parse(rawText);
  } catch (error) {
    const detail = error instanceof Error ? error.message : String(error);
    fail(`Invalid JSON in ${configPath}: ${detail}`);
  }

  if (raw === null || typeof raw !== "object" || Array.isArray(raw)) {
    fail("Config root must be a JSON object");
  }

  const data = raw as Record<string, unknown>;
  const mcpUrl = asNonEmptyString(data.mcpUrl, "mcpUrl");
  if (!isHttpUrl(mcpUrl)) {
    fail("mcpUrl must be an http(s) URL");
  }

  const callbackPort =
    data.callbackPort === undefined ? DEFAULTS.callbackPort : data.callbackPort;
  if (
    typeof callbackPort !== "number" ||
    !Number.isInteger(callbackPort) ||
    callbackPort < 1 ||
    callbackPort > 65535
  ) {
    fail("callbackPort must be an integer between 1 and 65535");
  }

  const callbackPath =
    data.callbackPath === undefined
      ? DEFAULTS.callbackPath
      : asNonEmptyString(data.callbackPath, "callbackPath");
  if (!callbackPath.startsWith("/")) {
    fail("callbackPath must start with /");
  }

  const host =
    data.host === undefined ? DEFAULTS.host : asNonEmptyString(data.host, "host");

  if (data.staticOAuthClientInfo === undefined) {
    fail("staticOAuthClientInfo is required");
  }

  return {
    mcpUrl,
    callbackPort,
    callbackPath,
    host,
    staticOAuthClientInfo: parseClientInfo(data.staticOAuthClientInfo, configPath),
    allowHttp: data.allowHttp === undefined ? DEFAULTS.allowHttp : Boolean(data.allowHttp),
    debug: data.debug === undefined ? DEFAULTS.debug : Boolean(data.debug),
  };
}

export function toMcpRemoteArgv(config: BridgeConfig): string[] {
  const clientInfoArg =
    typeof config.staticOAuthClientInfo === "string"
      ? config.staticOAuthClientInfo
      : JSON.stringify(config.staticOAuthClientInfo);

  const args = [
    config.mcpUrl,
    String(config.callbackPort),
    "--host",
    config.host,
    "--callback-path",
    config.callbackPath,
    "--static-oauth-client-info",
    clientInfoArg,
  ];

  if (config.allowHttp) {
    args.push("--allow-http");
  }
  if (config.debug) {
    args.push("--debug");
  }

  return args;
}
