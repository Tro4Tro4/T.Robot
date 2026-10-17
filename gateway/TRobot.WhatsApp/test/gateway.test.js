import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import { randomUUID } from 'node:crypto';
import { request as httpRequest } from 'node:http';
import { SendJournal, GatewayError } from '../src/journal.js';
import { WhatsAppGateway, normalizeSender } from '../src/gateway.js';
import { createCallback, createGatewayServer } from '../src/http.js';

const token = 'a'.repeat(64); // Synthetic test token, never used by the local configuration.
const group = '12345@g.us'; const otherGroup = '67890@g.us';
const now = 1700000000000;
async function harness(t, options = {}) {
  const directory = await mkdtemp(join(tmpdir(), 'trobot-gateway-test-'));
  assert.ok(resolve(directory).startsWith(resolve(tmpdir()) + sep));
  t.after(() => rm(directory, { recursive: true, force: true }));
  const sockets = []; const callbacks = []; const scheduled = []; let time = now;
  const timers = { setTimeout: (action, delay) => { const timer = { action, delay }; scheduled.push(timer); return timer; },
    clearTimeout: timer => { timer.cancelled = true; } };
  const journal = new SendJournal(directory, token);
  const transportFactory = async handlers => {
    const socket = { handlers, sent: [], reactions: [], closed: 0,
      groups: async () => [{ id: group, name: 'Gruppo sintetico' }, { id: otherGroup, name: 'Altro sintetico' }],
      members: async () => [{ id: '111@lid', alternateIds: ['222@s.whatsapp.net'], label: 'Membro sintetico' }],
      send: async (id, text) => { socket.sent.push([id, text]); return 'technical-message'; },
      react: async (...args) => { socket.reactions.push(args); }, close: () => { socket.closed++; }
    };
    sockets.push(socket); return socket;
  };
  const gateway = new WhatsAppGateway({ token, journal, transportFactory,
    callback: async payload => { callbacks.push(payload); return { handled: true, duplicate: false, reaction: '✅' }; },
    now: () => time, timers, ...options });
  return { gateway, journal, sockets, callbacks, scheduled, directory, setTime: value => { time = value; },
    async ready() { await gateway.connect(); sockets.at(-1).handlers.update({ connection: 'open' }); await gateway.group(group); } };
}
const incoming = (id = 'incoming-synthetic', overrides = {}) => ({
  key: { remoteJid: group, participant: '111:4@lid', participantAlt: '222:5@s.whatsapp.net', id, fromMe: false },
  message: { conversation: '!presenza si' }, messageTimestamp: now / 1000, ...overrides
});
const request = (server, path, { method = 'GET', authorization = 'Bearer ' + token, body, headers = {} } = {}) =>
  new Promise((resolve, reject) => {
    const client = httpRequest({ hostname: '127.0.0.1', port: server.address().port, path, method,
      headers: { ...(authorization === null ? {} : { Authorization: authorization }),
        ...(body === undefined ? {} : { 'Content-Type': 'application/json' }), ...headers } }, response => {
      let data = ''; response.setEncoding('utf8'); response.on('data', chunk => { data += chunk; });
      response.on('end', () => resolve({ status: response.statusCode, body: JSON.parse(data), headers: response.headers }));
    });
    client.on('error', reject); client.end(body === undefined ? undefined : typeof body === 'string' ? body : JSON.stringify(body));
  });

test('HTTP host starts disconnected and every endpoint requires constant-time bearer authorization', async t => {
  const h = await harness(t); const server = createGatewayServer(h.gateway, token);
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve)); t.after(() => new Promise(resolve => server.close(resolve)));
  for (const path of ['/health', '/groups', '/groups/12345%40g.us/members', '/connect', '/disconnect', '/group', '/send']) {
    assert.equal((await request(server, path, { authorization: null })).status, 401);
    assert.equal((await request(server, path, { authorization: 'Bearer ' + 'b'.repeat(64) })).status, 401);
  }
  const health = await request(server, '/health');
  assert.equal(health.status, 200); assert.equal(health.body.status, 'disconnected'); assert.equal(h.sockets.length, 0);
  assert.equal(health.headers['cache-control'], 'no-store');
  assert.equal((await request(server, '/health', { headers: { Host: 'external.invalid' } })).status, 403);
  assert.equal((await request(server, '/health', { headers: { Origin: 'http://external.invalid' } })).status, 403);
  assert.equal((await request(server, '/connect', { method: 'POST', body: {}, headers: { 'Content-Type': 'text/plain' } })).status, 415);
  assert.equal((await request(server, '/connect', { method: 'POST', body: '{' })).status, 400);
  assert.equal((await request(server, '/connect', { method: 'POST', body: 'x'.repeat(9000) })).status, 413);
  assert.equal((await request(server, '/connect', { method: 'POST', body: { unexpected: true } })).status, 400);
  assert.equal((await request(server, '/connect', { method: 'POST', body: {} })).body.status, 'connecting');
  assert.equal(h.sockets.length, 1);
  assert.equal((await request(server, '/disconnect', { method: 'POST', body: {} })).body.status, 'disconnected');
});

test('Explicit lifecycle, QR is health-only, pin validates membership and members need active group', async t => {
  const h = await harness(t);
  await h.gateway.connect(); assert.equal(h.gateway.health().status, 'connecting');
  h.sockets[0].handlers.update({ qrDataUrl: 'data:image/png;base64,synthetic' });
  assert.equal(h.gateway.health().status, 'qr');
  h.sockets[0].handlers.update({ connection: 'open' });
  assert.equal(h.gateway.health().qrDataUrl, null);
  await assert.rejects(h.gateway.group('111@s.whatsapp.net'), GatewayError);
  await assert.rejects(h.gateway.group('99999@g.us'), e => e.code === 'group_not_available');
  await assert.rejects(h.gateway.members(group), e => e.code === 'inactive_group');
  await h.gateway.group(group);
  assert.deepEqual((await h.gateway.members(group)).members[0].alternateIds, ['222@s.whatsapp.net']);
  await assert.rejects(h.gateway.members(otherGroup), e => e.code === 'inactive_group');
  h.gateway.disconnect(); assert.equal(h.gateway.health().activeGroupId, null);
  await h.gateway.closing;
  assert.equal(h.sockets[0].closed, 1);
});

test('Journal is durable before send, identical concurrent retries send once and changed payload conflicts', async t => {
  const h = await harness(t); await h.ready(); const operationId = randomUUID();
  h.sockets[0].send = async () => {
    const files = await readdir(h.directory); assert.equal(files.length, 1);
    const record = JSON.parse(await readFile(join(h.directory, files[0]), 'utf8'));
    assert.equal(record.status, 'pending');
    h.sockets[0].sent.push('one'); return 'technical-message';
  };
  const payload = { operationId, groupId: group, text: 'Messaggio sintetico privato' };
  const results = await Promise.all(Array.from({ length: 10 }, () => h.gateway.send(payload)));
  assert.ok(results.every(x => x.status === 'sent')); assert.equal(h.sockets[0].sent.length, 1);
  await assert.rejects(h.gateway.send({ ...payload, text: 'Differente' }), e => e.code === 'operation_conflict');
  const file = await readFile(join(h.directory, (await readdir(h.directory))[0]), 'utf8');
  for (const privateValue of [payload.text, group, operationId, token]) assert.ok(!file.includes(privateValue));
  h.gateway.disconnect(); assert.equal((await h.gateway.send(payload)).status, 'sent');
});

test('Restart pending and ambiguous transport failures never resend; journal failure prevents transport', async t => {
  const h = await harness(t); const payload = { operationId: randomUUID(), groupId: group, text: 'Sintetico' };
  await h.journal.pending(payload.operationId, h.journal.fingerprint(group, payload.text));
  assert.equal((await h.gateway.send(payload)).status, 'uncertain'); assert.equal(h.sockets.length, 0);
  await h.ready(); h.sockets[0].send = async () => { h.sockets[0].sent.push('attempt'); throw new Error('transport ambiguous'); };
  const next = { ...payload, operationId: randomUUID() };
  assert.equal((await h.gateway.send(next)).status, 'uncertain');
  assert.equal((await h.gateway.send(next)).status, 'uncertain'); assert.equal(h.sockets[0].sent.length, 1);
  h.journal.pending = async () => { throw new GatewayError('journal_unavailable', 503); };
  await assert.rejects(h.gateway.send({ ...payload, operationId: randomUUID() }), e => e.code === 'journal_unavailable');
  assert.equal(h.sockets[0].sent.length, 1);
});

test('Send rejects inactive groups and invalid payloads; repin while journal persists fences external send', async t => {
  const h = await harness(t); await h.ready(); const payload = { operationId: randomUUID(), groupId: group, text: 'Sintetico' };
  await assert.rejects(h.gateway.send({ ...payload, groupId: otherGroup }), e => e.code === 'inactive_group');
  await assert.rejects(h.gateway.send({ ...payload, text: 'x'.repeat(1001) }), e => e.code === 'invalid_send');
  const original = h.journal.pending.bind(h.journal);
  h.journal.pending = async (...args) => { await original(...args); await h.gateway.group(otherGroup); };
  assert.equal((await h.gateway.send(payload)).status, 'uncertain'); assert.equal(h.sockets[0].sent.length, 0);
});

test('Incoming only notify, pinned group, participant, fresh plain !presenza; PN/LID aliases normalized', async t => {
  const h = await harness(t); await h.ready(); const emit = batch => h.sockets[0].handlers.messages(batch);
  const rejects = [
    { type: 'append', messages: [incoming()] },
    { type: 'notify', messages: [incoming('own', { key: { ...incoming().key, fromMe: true } })] },
    { type: 'notify', messages: [incoming('dm', { key: { ...incoming().key, remoteJid: '222@s.whatsapp.net' } })] },
    { type: 'notify', messages: [incoming('other', { key: { ...incoming().key, remoteJid: otherGroup } })] },
    { type: 'notify', messages: [incoming('participant', { key: { ...incoming().key, participant: null } })] },
    { type: 'notify', messages: [incoming('old', { messageTimestamp: now / 1000 - 121 })] },
    { type: 'notify', messages: [incoming('beforeactivation', { messageTimestamp: now / 1000 - 1 })] },
    { type: 'notify', messages: [incoming('future', { messageTimestamp: now / 1000 + 31 })] },
    { type: 'notify', messages: [incoming('media', { message: { imageMessage: { caption: '!presenza si' } } })] },
    { type: 'notify', messages: [incoming('othercommand', { message: { conversation: '!menu' } })] },
    { type: 'notify', messages: [incoming('prefix', { message: { conversation: '!presenzaqualcosa' } })] },
    { type: 'notify', messages: [incoming('long', { message: { conversation: '!presenza ' + 's'.repeat(100) } })] }
  ];
  for (const batch of rejects) emit(batch);
  await h.gateway.idle(); assert.equal(h.callbacks.length, 0);
  emit({ type: 'notify', messages: [incoming(), incoming()] });
  await h.gateway.idle(); assert.equal(h.callbacks.length, 1);
  assert.deepEqual(h.callbacks[0].senderIds, ['111@lid', '222@s.whatsapp.net']);
  assert.equal(h.sockets[0].reactions.length, 1); assert.equal(h.sockets[0].reactions[0][2], '✅');
  assert.equal(normalizeSender('status@broadcast'), null);
});

test('Callbacks are serialized and bounded per sender; duplicate callback has no reaction', async t => {
  const h = await harness(t, { callback: async () => ({ handled: true, duplicate: true, reaction: '✅' }) }); await h.ready();
  h.sockets[0].handlers.messages({ type: 'notify', messages: Array.from({ length: 20 }, (_, i) => incoming('rate-' + i)) });
  await h.gateway.idle(); assert.equal(h.gateway.rates.values().next().value.count, 6);
  assert.equal(h.sockets[0].reactions.length, 0); assert.equal(h.gateway.queued, 0);
  h.setTime(now + 61000);
  h.sockets[0].handlers.messages({ type: 'notify', messages: [incoming('fresh', { messageTimestamp: (now + 61000) / 1000 })] });
  await h.gateway.idle(); assert.equal(h.gateway.rates.values().next().value.count, 1);
});

test('Global callback queue stays bounded even across many distinct senders', async t => {
  let release; const response = new Promise(resolve => { release = resolve; }); let count = 0;
  const h = await harness(t, { callback: async () => { count++; return response; } }); await h.ready();
  const messages = Array.from({ length: 100 }, (_, i) => incoming('bounded-' + i, {
    key: { ...incoming().key, id: 'bounded-' + i, participant: String(1000 + i) + '@lid' }
  }));
  h.sockets[0].handlers.messages({ type: 'notify', messages });
  assert.equal(h.gateway.queued, 50);
  release({ handled: false, duplicate: false, reaction: null }); await h.gateway.idle();
  assert.equal(count, 50); assert.equal(h.gateway.queued, 0); assert.equal(h.sockets[0].reactions.length, 0);
});

test('Disconnect and pin/socket epochs fence in-flight callback reactions and queued callbacks', async t => {
  let release; let entered;
  const started = new Promise(resolve => { entered = resolve; });
  const response = new Promise(resolve => { release = resolve; });
  const h = await harness(t, { callback: async () => { entered(); return response; } }); await h.ready();
  h.sockets[0].handlers.messages({ type: 'notify', messages: [incoming(), incoming('queued')] });
  await started; h.gateway.disconnect(); release({ handled: true, duplicate: false, reaction: '✅' });
  await h.gateway.idle(); assert.equal(h.sockets[0].reactions.length, 0); assert.equal(h.gateway.queued, 0);
  await h.ready();
  h.sockets[0].handlers.update({ connection: 'open' }); // Old socket may not change new lifecycle.
  assert.equal(h.gateway.health().status, 'connected');
  h.sockets[0].handlers.messages({ type: 'notify', messages: [incoming('stale-socket')] });
  await h.gateway.idle(); assert.equal(h.sockets[1].reactions.length, 0);
});

test('Reconnect backoff is bounded, stale sockets cannot recurse and logged-out stops retries', async t => {
  const h = await harness(t); await h.ready(); const original = h.sockets[0];
  for (let i = 0; i < 6; i++) {
    h.sockets.at(-1).handlers.update({ connection: 'close' });
    original.handlers.update({ connection: 'close' }); // stale close ignored
    if (i < 5) { assert.equal(h.scheduled[i].delay, Math.min(1000 * 2 ** i, 16000)); h.scheduled[i].action(); await h.gateway.startTail; }
  }
  assert.equal(h.gateway.health().errorCode, 'reconnect_exhausted');
  await h.gateway.connect(); h.sockets.at(-1).handlers.update({ connection: 'close', loggedOut: true });
  assert.equal(h.gateway.health().errorCode, 'session_logged_out'); assert.equal(h.gateway.requested, false);
});

test('Callback URL fixed loopback, authorization and only generic reaction accepted', async () => {
  assert.throws(() => createCallback('https://external.invalid/callback', token));
  let options;
  const callback = createCallback('http://127.0.0.1:5238/internal/whatsapp/incoming', token,
    async (_, request) => { options = request; return new Response(JSON.stringify({ handled: true, duplicate: false, reaction: 'private free text' })); });
  const result = await callback({ text: '!presenza si' });
  assert.equal(options.headers.Authorization, 'Bearer ' + token); assert.equal(options.redirect, 'error');
  assert.equal(result.handled, true); assert.equal(result.reaction, null);
});
