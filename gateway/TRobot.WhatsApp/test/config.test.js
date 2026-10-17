import test from 'node:test';
import assert from 'node:assert/strict';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { parseConfiguration } from '../src/config.js';

test('Config accepts Windows PowerShell UTF8 BOM and requires fixed loopback endpoints and private state directory', () => {
  const root = join(tmpdir(), 'trobot-synthetic-root');
  const config = { Enabled: true, BaseUrl: 'http://127.0.0.1:5240', Token: 'c'.repeat(64),
    CallbackUrl: 'http://127.0.0.1:5238/internal/whatsapp/incoming', StateDirectory: join(root, '.local/whatsapp/state') };
  assert.deepEqual(parseConfiguration('\uFEFF' + JSON.stringify({ WhatsAppGateway: config }), root), config);
  for (const override of [{ Enabled: 'true' }, { Token: '' }, { BaseUrl: 'http://external.invalid' },
    { CallbackUrl: 'http://127.0.0.1:5238/unexpected' }, { StateDirectory: root }])
    assert.throws(() => parseConfiguration(JSON.stringify({ WhatsAppGateway: { ...config, ...override } }), root));
});
