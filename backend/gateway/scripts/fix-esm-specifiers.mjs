import fs from "node:fs/promises";
import path from "node:path";

const projectRoot = process.cwd();
const distRoot = path.join(projectRoot, "dist");

const importPattern =
  /((?:import|export)\s+(?:[^"'`]+\s+from\s+)?["'])(\.\.?(?:\/[^"'`]+)+)(["'])/g;

async function walk(directory) {
  const entries = await fs.readdir(directory, { withFileTypes: true });
  for (const entry of entries) {
    const fullPath = path.join(directory, entry.name);
    if (entry.isDirectory()) {
      await walk(fullPath);
      continue;
    }

    if (entry.isFile() && fullPath.endsWith(".js")) {
      await patchFile(fullPath);
    }
  }
}

function needsJsExtension(specifier) {
  const extension = path.extname(specifier);
  return extension.length === 0;
}

async function patchFile(filePath) {
  const original = await fs.readFile(filePath, "utf8");
  let changed = false;

  const updated = original.replace(importPattern, (fullMatch, prefix, specifier, suffix) => {
    if (!needsJsExtension(specifier)) {
      return fullMatch;
    }

    changed = true;
    return `${prefix}${specifier}.js${suffix}`;
  });

  if (changed) {
    await fs.writeFile(filePath, updated, "utf8");
  }
}

await walk(distRoot);
