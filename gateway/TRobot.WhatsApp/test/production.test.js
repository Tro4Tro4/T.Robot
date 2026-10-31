import test from 'node:test';
import assert from 'node:assert/strict';
import { createServer } from 'node:https';
import { once } from 'node:events';
import { mkdtemp, rm, cp } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { parseConfiguration, assertPrivatePath, loadConfiguration } from '../src/config.js';
import { createCallback } from '../src/http.js';
import { SendJournal } from '../src/journal.js';
import { WhatsAppGateway } from '../src/gateway.js';
import { certificate, key, wrongHostnameCertificate } from './tls-fixture.js';

const token = 'a'.repeat(64);
const callbackUrl = 'https://localhost:5443/internal/whatsapp/incoming';
const config = { Enabled: true, ProductionEnabled: true, Token: token, BaseUrl: 'http://127.0.0.1:5240',
  StateDirectory: '/var/lib/trobot-whatsapp/state', CallbackCaPath: '/etc/trobot-whatsapp/localhost.crt',
  CallbackUrl: callbackUrl };
const parse = value => parseConfiguration(JSON.stringify({ WhatsAppGateway: value }), '/opt/trobot', 'Production');

test('Production requires explicit opt-in and exact private paths and endpoints without changing token', () => {
  const result = parse(config);
  assert.equal(result.Token, token);
  assert.ok(Object.isFrozen(result));
  assert.throws(() => { result.Token = 'b'.repeat(64); }, TypeError);
  for (const override of [{ Enabled: false }, { ProductionEnabled: undefined }, { ProductionEnabled: false },
    { Token: 'g'.repeat(64) }, { Token: 'a'.repeat(63) }, { BaseUrl: 'http://localhost:5240' },
    { BaseUrl: config.BaseUrl + '/' }, { StateDirectory: '/var/lib/trobot-whatsapp/other/../state' },
    { StateDirectory: '/opt/trobot/wwwroot/state' }, { CallbackCaPath: '/tmp/ca.pem' },
    ...['?x=1', '#x', '/'].map(suffix => ({ CallbackUrl: callbackUrl + suffix })),
    { CallbackUrl: 'https://user@localhost:5443/internal/whatsapp/incoming' },
    { CallbackUrl: 'http://127.0.0.1:5238/internal/whatsapp/incoming' }])
    assert.throws(() => parse({ ...config, ...override }), /invalid_configuration/);
});

test('Production does not fall back to a local config or accept alternate config paths', async () => {
  for (const path of [undefined, '/tmp/gateway.json', '/etc/trobot-whatsapp/../trobot-whatsapp/gateway.json'])
    await assert.rejects(loadConfiguration('/opt/trobot', 'Production', path), /invalid_configuration/);
});

test('Production protected path validation rejects links and writable ancestors', async () => {
  const target = join(tmpdir(), 'synthetic', 'config.json');
  const regular = candidate => ({ uid: 0, mode: 0o755, isSymbolicLink: () => false,
    isDirectory: () => candidate !== target, isFile: () => candidate === target });
  await assertPrivatePath(target, false, async candidate => regular(candidate));
  for (const issue of ['link', 'writable', 'owner'])
    await assert.rejects(assertPrivatePath(target, false, async candidate => ({ ...regular(candidate),
      mode: issue === 'writable' ? 0o777 : 0o755,
      uid: issue === 'owner' ? 1000 : 0,
      isSymbolicLink: () => issue === 'link' })), /invalid_configuration/);
});

test('HTTPS callback checks explicit trust and hostname and refuses redirects and malformed replies', async t => {
  let status = 200; let reply = { handled: true, duplicate: false, reaction: null }; let calls = 0;
  const server = createServer({ cert: certificate, key }, (request, response) => {
    calls++;
    assert.equal(request.headers.authorization, 'Bearer ' + token);
    assert.equal(request.url, '/internal/whatsapp/incoming');
    response.writeHead(status, { Location: 'https://example.invalid/' }); response.end(JSON.stringify(reply));
  });
  server.listen(5443, '127.0.0.1'); await once(server, 'listening');
  t.after(() => new Promise(resolve => server.close(resolve)));
  assert.throws(() => createCallback(callbackUrl, token), /invalid_callback_configuration/);
  const callback = createCallback(callbackUrl, token, undefined, { ca: Buffer.from(certificate) });
  assert.equal((await callback({ text: 'synthetic' })).handled, true);
  // An explicit but unrelated trust set must fail before HTTP reaches the server.
  const { rootCertificates } = await import('node:tls');
  const bad = createCallback(callbackUrl, token, undefined, { ca: Buffer.from(rootCertificates[0]) });
  await assert.rejects(bad({}), /callback_unavailable/);
  assert.equal(calls, 1);
  status = 302; await assert.rejects(callback({}), /callback_unavailable/);
  assert.equal(calls, 2);
  status = 200; reply = { unexpected: true }; await assert.rejects(callback({}), /callback_invalid/);
  server.setSecureContext({ cert: wrongHostnameCertificate, key });
  const wrongName = createCallback(callbackUrl, token, undefined, { ca: Buffer.from(wrongHostnameCertificate) });
  await assert.rejects(wrongName({}), /callback_unavailable/);
  assert.equal(calls, 3);
});

test('Migrated journal remains compatible with preserved token and startup stays disconnected', async t => {
  const root = await mkdtemp(join(tmpdir(), 'trobot-migration-'));
  t.after(() => rm(root, { recursive: true, force: true }));
  const original = new SendJournal(join(root, 'original'), token);
  const operation = '11111111-1111-4111-8111-111111111111';
  const digest = original.fingerprint('123@g.us', 'synthetic');
  await original.pending(operation, digest);
  await original.complete(operation, digest, { status: 'sent', messageId: 'synthetic-id' });
  await cp(join(root, 'original'), join(root, 'remote'), { recursive: true });
  const migrated = new SendJournal(join(root, 'remote'), parse(config).Token);
  assert.equal(migrated.fingerprint('123@g.us', 'synthetic'), digest);
  assert.deepEqual(await migrated.get(operation, digest), { status: 'sent', messageId: 'synthetic-id' });
  const gateway = new WhatsAppGateway({ token, journal: migrated,
    transportFactory: () => { throw new Error('must_not_connect'); }, callback: () => {} });
  assert.equal(gateway.status, 'disconnected'); assert.equal(gateway.activeGroupId, null);
});
