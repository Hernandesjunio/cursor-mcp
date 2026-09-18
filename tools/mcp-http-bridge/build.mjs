import * as esbuild from "esbuild";
import { mkdirSync, readFileSync } from "node:fs";
import { dirname, join } from "node:path";
import { fileURLToPath } from "node:url";

const root = dirname(fileURLToPath(import.meta.url));

mkdirSync(join(root, "dist"), { recursive: true });

/** CJS bundle empties `import.meta`; `open` needs a real file URL at init. */
const importMetaUrlPlugin = {
  name: "import-meta-url-cjs",
  setup(build) {
    build.onLoad({ filter: /[\\/]node_modules[\\/]open[\\/].*\.js$/ }, (args) => ({
      contents: readFileSync(args.path, "utf8").replaceAll(
        "import.meta.url",
        "require('node:url').pathToFileURL(__filename).href",
      ),
      loader: "js",
    }));
  },
};

await esbuild.build({
  absWorkingDir: root,
  entryPoints: ["src/cli.ts"],
  bundle: true,
  platform: "node",
  format: "cjs",
  target: "node18",
  outfile: "dist/mcp-http-bridge.js",
  logLevel: "info",
  legalComments: "none",
  minify: false,
  plugins: [importMetaUrlPlugin],
});

console.log("Wrote dist/mcp-http-bridge.js");
