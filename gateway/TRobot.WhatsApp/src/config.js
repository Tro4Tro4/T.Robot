import { isAbsolute, join, relative, resolve } from 'node:path';

export function parseConfiguration(json, root) {
  // Windows PowerShell 5 emits a UTF-8 BOM with Set-Content -Encoding utf8.
  const config = JSON.parse(json.replace(/^\uFEFF/, '')).WhatsAppGateway;
  if (!config || typeof config !== 'object') throw new Error('invalid_configuration');
  const stateRelative = typeof config.StateDirectory === 'string' && isAbsolute(config.StateDirectory)
    ? relative(join(root, '.local/whatsapp'), resolve(config.StateDirectory)) : '..';
  if (config.Enabled !== true || config.BaseUrl !== 'http://127.0.0.1:5240'
    || typeof config.Token !== 'string' || !/^[a-f0-9]{64}$/i.test(config.Token)
    || !stateRelative || stateRelative.startsWith('..') || isAbsolute(stateRelative)
    || config.CallbackUrl !== 'http://127.0.0.1:5238/internal/whatsapp/incoming')
    throw new Error('invalid_configuration');
  return config;
}
