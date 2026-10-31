import { isAbsolute, join, relative, resolve } from 'node:path';
import { lstat, readFile } from 'node:fs/promises';

export const productionConfigPath = '/etc/trobot-whatsapp/gateway.json';
export const productionCaPath = '/etc/trobot-whatsapp/localhost.crt';
export const productionStatePath = '/var/lib/trobot-whatsapp/state';

export function parseConfiguration(json, root, environment = 'Development') {
  // Windows PowerShell 5 emits a UTF-8 BOM with Set-Content -Encoding utf8.
  const config = JSON.parse(json.replace(/^\uFEFF/, '')).WhatsAppGateway;
  if (!config || typeof config !== 'object') throw new Error('invalid_configuration');
  if (!['Development', 'Production'].includes(environment)) throw new Error('invalid_configuration');
  if (environment === 'Production') {
    if (config.Enabled !== true || config.ProductionEnabled !== true
      || config.BaseUrl !== 'http://127.0.0.1:5240'
      || typeof config.Token !== 'string' || !/^[a-f0-9]{64}$/i.test(config.Token)
      || config.StateDirectory !== productionStatePath || config.CallbackCaPath !== productionCaPath
      || config.CallbackUrl !== 'https://localhost:5443/internal/whatsapp/incoming')
      throw new Error('invalid_configuration');
    return Object.freeze({ ...config });
  }
  const stateRelative = typeof config.StateDirectory === 'string' && isAbsolute(config.StateDirectory)
    ? relative(join(root, '.local/whatsapp'), resolve(config.StateDirectory)) : '..';
  if (config.Enabled !== true || config.BaseUrl !== 'http://127.0.0.1:5240'
    || typeof config.Token !== 'string' || !/^[a-f0-9]{64}$/i.test(config.Token)
    || !stateRelative || stateRelative.startsWith('..') || isAbsolute(stateRelative)
    || config.CallbackUrl !== 'http://127.0.0.1:5238/internal/whatsapp/incoming')
    throw new Error('invalid_configuration');
  return Object.freeze({ ...config });
}

// Deployment provisions these paths before startup. Never follow symlinks or create replacement state.
export async function assertPrivatePath(path, directory, inspect = lstat) {
  const parents = []; let current = resolve(path);
  for (;;) {
    parents.push(current); const parent = resolve(current, '..');
    if (parent === current) break;
    current = parent;
  }
  for (const candidate of parents.reverse()) {
    const stat = await inspect(candidate);
    if (stat.isSymbolicLink() || (candidate === path && (directory ? !stat.isDirectory() : !stat.isFile()))
      || (stat.mode & 0o022) !== 0 || (!directory && stat.uid !== 0)
      || (directory && candidate === path && (stat.mode & 0o077) !== 0))
      throw new Error('invalid_configuration');
  }
}

export async function loadConfiguration(root, environment, explicitPath) {
  if (environment === 'Production') {
    if (explicitPath !== productionConfigPath) throw new Error('invalid_configuration');
    await assertPrivatePath(explicitPath, false);
  }
  const path = explicitPath ?? join(root, '.local/whatsapp/gateway.json');
  const config = parseConfiguration(await readFile(path, 'utf8'), root, environment);
  if (environment === 'Production') {
    await assertPrivatePath(config.StateDirectory, true);
    await assertPrivatePath(config.CallbackCaPath, false);
    // Existing journals/sessions may have been copied; reject unexpected links before transport can access them.
    await inspectState(config.StateDirectory);
  }
  return config;
}

async function inspectState(directory) {
  const { readdir } = await import('node:fs/promises');
  for (const name of await readdir(directory)) {
    const path = join(directory, name); const stat = await lstat(path);
    if (stat.isSymbolicLink() || (!stat.isFile() && !stat.isDirectory()) || (stat.mode & 0o077) !== 0)
      throw new Error('invalid_configuration');
    if (stat.isDirectory()) await inspectState(path);
  }
}
