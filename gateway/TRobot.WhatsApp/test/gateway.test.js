import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, readFile, readdir, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, resolve, sep } from 'node:path';
import { randomUUID } from 'node:crypto';
import { request as httpRequest } from 'node:http';
import { SendJournal, GatewayError } from '../src/journal.js';
import { WhatsAppGateway, normalizeSender, extractPlainText, validateCallback } from '../src/gateway.js';
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
    const socket = { handlers, sent: [], privateSent: [], reactions: [], closed: 0,
      groups: async () => [{ id: group, name: 'Gruppo sintetico' }, { id: otherGroup, name: 'Altro sintetico' }],
      members: async () => [{ id: '111@lid', alternateIds: ['222@s.whatsapp.net'], label: 'Membro sintetico' }],
      send: async (id, text) => { socket.sent.push([id, text]); return 'technical-message'; },
      sendPrivate: async (id, text) => { socket.privateSent.push([id, text]); return 'private-technical-message'; },
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
    { type: 'notify', messages: [incoming('broadcast', { key: { ...incoming().key, remoteJid: 'status@broadcast' } })] },
    { type: 'notify', messages: [incoming('other', { key: { ...incoming().key, remoteJid: otherGroup } })] },
    { type: 'notify', messages: [incoming('participant', { key: { ...incoming().key, participant: null } })] },
    { type: 'notify', messages: [incoming('old', { messageTimestamp: now / 1000 - 121 })] },
    { type: 'notify', messages: [incoming('beforeactivation', { messageTimestamp: now / 1000 - 1 })] },
    { type: 'notify', messages: [incoming('future', { messageTimestamp: now / 1000 + 31 })] },
    { type: 'notify', messages: [incoming('media', { message: { imageMessage: { caption: '!presenza si' } } })] },
    { type: 'notify', messages: [incoming('othercommand', { message: { conversation: '!budget' } })] },
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

test('Real decoded group text with sender-key metadata and ephemeral text reach the callback without metadata', async t => {
  const h = await harness(t); await h.ready();
  // rc14 decode-wa-message processes this metadata but retains it in fullMessage.message.
  const senderKeyDistributionMessage = { groupId: group, axolotlSenderKeyDistributionMessage: Buffer.from('synthetic-key') };
  const contents = [
    { conversation: '!presenza si', senderKeyDistributionMessage },
    { extendedTextMessage: { text: '!presenza forse', contextInfo: { stanzaId: 'synthetic-quote' } }, senderKeyDistributionMessage },
    { ephemeralMessage: { message: { conversation: '!presenza no', senderKeyDistributionMessage } }, senderKeyDistributionMessage },
    { ephemeralMessage: { message: { extendedTextMessage: { text: '!presenza si' }, messageContextInfo: {} } }, messageContextInfo: {} }
  ];
  h.sockets[0].handlers.messages({ type: 'notify', messages: contents.map((message, i) => incoming('decoded-' + i, { message })) });
  await h.gateway.idle();
  assert.deepEqual(h.callbacks.map(x => x.text), ['!presenza si', '!presenza forse', '!presenza no', '!presenza si']);
  assert.ok(h.callbacks.every(x => !('message' in x) && !('senderKeyDistributionMessage' in x)));
  assert.equal(h.sockets[0].reactions.length, 4);
  const diagnostics = h.gateway.health().diagnostics;
  assert.equal(diagnostics.received, 4); assert.equal(diagnostics.envelopeRejected, 0);
  assert.equal(diagnostics.callbackHandled, 4); assert.equal(diagnostics.reactionSent, 4);
  for (const privateValue of [group, 'decoded-', '!presenza', 'synthetic-key', token])
    assert.ok(!JSON.stringify(diagnostics).includes(privateValue));
  diagnostics.received = 999; assert.equal(h.gateway.health().diagnostics.received, 4);
});

test('Plain-text extraction never unwraps edits, view-once, protocol or media and limits disappearing wrapper depth', async t => {
  const h = await harness(t); await h.ready();
  const text = { conversation: '!presenza si' };
  const rejected = [
    { senderKeyDistributionMessage: {} },
    { imageMessage: { caption: '!presenza si' }, senderKeyDistributionMessage: {} },
    { ephemeralMessage: { message: { imageMessage: { caption: '!presenza si' } } } },
    { editedMessage: { message: text } },
    { viewOnceMessage: { message: text } },
    { ephemeralMessage: { message: { viewOnceMessageV2: { message: text } } } },
    { documentWithCaptionMessage: { message: text } },
    { conversation: '!presenza si', protocolMessage: {} },
    { conversation: '!presenza si', extendedTextMessage: { text: '!presenza no' } }
  ];
  let nested = text;
  for (let i = 0; i < 5; i++) nested = { ephemeralMessage: { message: nested } };
  assert.equal(extractPlainText(nested), '!presenza si');
  rejected.push({ ephemeralMessage: { message: nested } });
  for (const content of rejected) assert.equal(extractPlainText(content), null);
  h.sockets[0].handlers.messages({ type: 'notify', messages: rejected.map((message, i) => incoming('rejected-envelope-' + i, { message })) });
  await h.gateway.idle(); assert.equal(h.callbacks.length, 0);
  assert.equal(h.gateway.health().diagnostics.envelopeRejected, rejected.length);
});

test('RAM diagnostics distinguish callback failure, ignored command and reaction failure without exception details', async t => {
  const h = await harness(t, { callback: async () => { throw new Error('private exception details'); } }); await h.ready();
  const emit = id => h.sockets[0].handlers.messages({ type: 'notify', messages: [incoming(id)] });
  emit('failure'); await h.gateway.idle();
  h.gateway.callback = async () => ({ handled: false, duplicate: false, reaction: null });
  emit('ignored'); await h.gateway.idle();
  h.gateway.callback = async () => ({ handled: true, duplicate: false, reaction: '✅' });
  h.sockets[0].react = async () => { throw new Error('private reaction details'); };
  emit('reaction-failure'); await h.gateway.idle();
  const diagnostics = h.gateway.health().diagnostics;
  assert.equal(diagnostics.callbackAttempts, 3); assert.equal(diagnostics.callbackFailed, 1);
  assert.equal(diagnostics.callbackIgnored, 1); assert.equal(diagnostics.callbackHandled, 1);
  assert.equal(diagnostics.reactionFailed, 1); assert.equal(diagnostics.reactionSent, 0);
  assert.ok(!JSON.stringify(diagnostics).includes('private'));
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

test('Operational whitelist uses the existing callback and rejects unrelated prefixes', async t => {
  const h = await harness(t); await h.ready();
  for (const [i, text] of ['!porto abcdef12', '!PRENDO abcdef12', '!fatto abcdef12', '!conferma ' + 'a'.repeat(32), '!portone abcdef12', '!vota 123 1', '!spesa 10'].entries()) {
    h.sockets[0].handlers.messages({ type: 'notify', messages: [incoming('ops-' + i, { message: { conversation: text } })] });
    await h.gateway.idle();
  }
  assert.equal(h.callbacks.length, 4); assert.equal(h.sockets[0].reactions.length, 4);
  assert.equal(h.gateway.health().diagnostics.commandRejected, 3);
});

test('Attendance candidates and cancellation reach Application with exact original text and no dialog state', async t => {
  const forwarded = [];
  const h = await harness(t, { callback: async payload => {
    forwarded.push(payload);
    // Application may ignore every candidate: the gateway neither opens nor requires a dialog.
    return { handled: false, duplicate: false, reaction: null };
  } }); await h.ready();
  const texts = ['30/12', '1.2.2027', '01-02-2027', '99/99/0000', '31/02', '0', '32', '999', '4', 'si', 'SÌ', 'NO', '  sì\n', ' 30/12/2026 ', '!annulla', '!ANNULLA'];
  h.sockets[0].handlers.messages({ type: 'notify', messages: texts.map((text, i) => incoming('attendance-' + i, {
    key: { ...incoming().key, id: 'attendance-' + i, participant: String(1000 + i) + '@lid' },
    message: { conversation: text }
  })) });
  await h.gateway.idle();
  assert.deepEqual(forwarded.map(x => x.text), texts);
  assert.equal(h.gateway.health().diagnostics.callbackIgnored, texts.length);
  assert.equal(h.sockets[0].sent.length, 0); assert.equal(h.sockets[0].reactions.length, 0);
  assert.deepEqual(await readdir(h.directory), []);
});

test('Attendance candidates do not widen forwarding to ordinary chat or change command prefix rules', async t => {
  const h = await harness(t); await h.ready();
  const rejected = ['ciao', 'arrivo il 30/12', '30/12 grazie', 'sì per tutti', 'no grazie', '1000', '-1', '3,5', '1/2/26', '2026/12/30', 'si?', 'sì!', '!annullamento', ' !annulla', '!vota 1 2', ' '.repeat(100) + '4'];
  h.sockets[0].handlers.messages({ type: 'notify', messages: rejected.map((text, i) => incoming('ordinary-' + i, { message: { conversation: text } })) });
  await h.gateway.idle();
  assert.equal(h.callbacks.length, 0);
  assert.equal(h.gateway.health().diagnostics.commandRejected, rejected.length);
});

test('Attendance candidates preserve live group sender timestamp and plain-text envelope filters', async t => {
  const h = await harness(t); await h.ready();
  const candidate = { ...incoming(), message: { conversation: '30/12' } };
  const rejects = [
    { type: 'append', messages: [candidate] },
    { type: 'notify', messages: [{ ...candidate, key: { ...candidate.key, fromMe: true } }] },
    { type: 'notify', messages: [{ ...candidate, key: { ...candidate.key, remoteJid: 'status@broadcast' } }] },
    { type: 'notify', messages: [{ ...candidate, key: { ...candidate.key, remoteJid: otherGroup } }] },
    { type: 'notify', messages: [{ ...candidate, key: { ...candidate.key, participant: null } }] },
    { type: 'notify', messages: [{ ...candidate, messageTimestamp: now / 1000 - 121 }] },
    { type: 'notify', messages: [{ ...candidate, messageTimestamp: now / 1000 - 1 }] },
    { type: 'notify', messages: [{ ...candidate, messageTimestamp: now / 1000 + 31 }] },
    { type: 'notify', messages: [{ ...candidate, message: { imageMessage: { caption: '30/12' } } }] },
    { type: 'notify', messages: [{ ...candidate, message: { editedMessage: { message: { conversation: '4' } } } }] }
  ];
  for (const batch of rejects) h.sockets[0].handlers.messages(batch);
  await h.gateway.idle(); assert.equal(h.callbacks.length, 0);
  h.sockets[0].handlers.messages({ type: 'notify', messages: [candidate, candidate] });
  await h.gateway.idle(); assert.equal(h.callbacks.length, 1);
  assert.equal(h.callbacks[0].text, '30/12');
});

test('Attendance candidates retain per-sender six-per-minute rate and fifty-message queue bounds', async t => {
  let release; const response = new Promise(resolve => { release = resolve; }); let count = 0;
  const h = await harness(t, { callback: async () => { count++; return response; } }); await h.ready();
  const messages = Array.from({ length: 8 }, (_, i) => incoming('candidate-rate-' + i, { message: { conversation: '4' } }));
  messages.push(...Array.from({ length: 60 }, (_, i) => incoming('candidate-bound-' + i, {
    key: { ...incoming().key, id: 'candidate-bound-' + i, participant: String(1000 + i) + '@lid' },
    message: { conversation: 'sì' }
  })));
  h.sockets[0].handlers.messages({ type: 'notify', messages });
  assert.equal(h.gateway.queued, 50);
  assert.equal(h.gateway.health().diagnostics.rateRejected, 2);
  release({ handled: false, duplicate: false, reaction: null }); await h.gateway.idle();
  assert.equal(count, 50); assert.equal(h.gateway.queued, 0);
});

test('Attendance candidate callbacks cannot survive same-group repin or a changed socket', async t => {
  const h = await harness(t); await h.ready();
  const originalSocket = h.sockets[0];
  let release; h.gateway.callbackTail = new Promise(resolve => { release = resolve; });
  originalSocket.handlers.messages({ type: 'notify', messages: [incoming('candidate-repin', { message: { conversation: '4' } })] });
  await h.gateway.group(group); release();
  await h.gateway.idle(); assert.equal(h.callbacks.length, 0);
  originalSocket.handlers.messages({ type: 'notify', messages: [incoming('candidate-reconnect', { message: { conversation: 'sì' } })] });
  h.gateway.disconnect(); await h.ready(); await h.gateway.idle();
  originalSocket.handlers.messages({ type: 'notify', messages: [incoming('candidate-stale', { message: { conversation: 'no' } })] });
  await h.gateway.idle(); assert.equal(h.callbacks.length, 0);
  assert.ok(h.sockets.every(socket => socket.sent.length === 0 && socket.reactions.length === 0));
});

test('Operational confirmation reply code never enters journal and duplicate suppresses resending', async t => {
  const code = 'b'.repeat(32); const operationId = randomUUID();
  const h = await harness(t, { callback: async () => ({ handled: true, duplicate: false, reaction: null,
    replyText: '!conferma ' + code, replyOperationId: operationId }) }); await h.ready();
  const emit = id => h.sockets[0].handlers.messages({ type: 'notify', messages: [incoming(id, { message: { conversation: '!porto abcdef12' } })] });
  emit('proposal'); await h.gateway.idle(); assert.equal(h.sockets[0].sent.length, 1);
  h.gateway.callback = async () => ({ handled: true, duplicate: true, reaction: null });
  emit('replayed'); await h.gateway.idle(); assert.equal(h.sockets[0].sent.length, 1);
  for (const file of await readdir(h.directory)) assert.ok(!(await readFile(join(h.directory, file), 'utf8')).includes(code));
});

test('Real informational commands reply through the persistent journal, never react or replay', async t => {
  const operationId = randomUUID(); const text = 'è'.repeat(2000);
  const h = await harness(t, { callback: async () => ({ handled: true, duplicate: false, reaction: null,
    replyText: text, replyOperationId: operationId }) }); await h.ready();
  const emit = (id, command) => h.sockets[0].handlers.messages({ type: 'notify', messages: [incoming(id, { message: { conversation: command } })] });
  emit('info-one', '!menu 2'); await h.gateway.idle();
  assert.deepEqual(h.sockets[0].sent, [[group, text]]); assert.equal(h.sockets[0].reactions.length, 0);
  emit('info-two', '!todo'); await h.gateway.idle(); assert.equal(h.sockets[0].sent.length, 1);
  h.gateway.callback = async () => ({ handled: true, duplicate: true, reaction: null, replyText: text, replyOperationId: randomUUID() });
  emit('info-three', '!aiuto'); await h.gateway.idle(); assert.equal(h.sockets[0].sent.length, 1);
  h.gateway.callback = async () => ({ handled: false, duplicate: false, reaction: null, replyText: text, replyOperationId: randomUUID() });
  emit('info-four', '!aiuto'); await h.gateway.idle(); assert.equal(h.sockets[0].sent.length, 1);
  const stored = await readFile(join(h.directory, (await readdir(h.directory))[0]), 'utf8');
  assert.ok(!stored.includes(text)); assert.ok(!stored.includes(group)); assert.ok(!stored.includes(operationId));
  await assert.rejects(h.gateway.send({ operationId: randomUUID(), groupId: group, text }), e => e.code === 'invalid_send');
});

test('Attendance summary forwards exact bounded commands and journals only one reply per operation', async t => {
  const operationId = randomUUID();
  const h = await harness(t); await h.ready();
  h.gateway.callback = async payload => {
    h.callbacks.push(payload);
    return { handled: true, duplicate: false, reaction: null, replyText: 'Riepilogo sintetico', replyOperationId: operationId };
  };
  for (const [index, text] of ['!riepilogo', '!RIEPILOGO 2'].entries()) {
    h.sockets[0].handlers.messages({ type: 'notify', messages: [incoming('summary-' + index, { message: { conversation: text } })] });
    await h.gateway.idle();
    assert.equal(h.callbacks[index].text, text);
    assert.equal(h.callbacks[index].groupId, group);
    assert.deepEqual(h.callbacks[index].senderIds, ['111@lid', '222@s.whatsapp.net']);
  }
  assert.deepEqual(h.sockets[0].sent, [[group, 'Riepilogo sintetico']]);
  assert.equal(h.sockets[0].reactions.length, 0);
  const stored = await readFile(join(h.directory, (await readdir(h.directory))[0]), 'utf8');
  assert.ok(!stored.includes('Riepilogo sintetico')); assert.ok(!stored.includes('!riepilogo'));
});

test('Attendance summary preserves channel, envelope, prefix and length filters', async t => {
  const h = await harness(t); await h.ready();
  const messages = [
    incoming('summary-prefix', { message: { conversation: '!riepilogo2' } }),
    incoming('summary-chat', { message: { conversation: 'riepilogo' } }),
    incoming('summary-long', { message: { conversation: '!riepilogo ' + '1'.repeat(91) } }),
    incoming('summary-other', { key: { remoteJid: otherGroup, participant: '111@lid', id: 'summary-other' }, message: { conversation: '!riepilogo' } }),
    incoming('summary-broadcast', { key: { remoteJid: 'status@broadcast', participant: '111@lid', id: 'summary-broadcast' }, message: { conversation: '!riepilogo' } }),
    incoming('summary-old', { messageTimestamp: now / 1000 - 121, message: { conversation: '!riepilogo' } }),
    incoming('summary-media', { message: { imageMessage: { caption: '!riepilogo' } } })
  ];
  h.sockets[0].handlers.messages({ type: 'notify', messages }); await h.gateway.idle();
  assert.equal(h.callbacks.length, 0); assert.equal(h.sockets[0].sent.length, 0);
  assert.equal(h.gateway.health().diagnostics.commandRejected, 3);
});

test('Callback reply fields are bounded and typed, mutually exclusive and cannot choose a recipient', async () => {
  const valid = { handled: true, duplicate: false, reaction: null, replyText: 'Risposta', replyOperationId: randomUUID() };
  for (const changed of [ { replyText: 3 }, { replyText: '' }, { replyText: ' '.repeat(3) }, { replyText: 'x'.repeat(2001) },
    { replyOperationId: 'not-guid' }, { replyOperationId: null }, { replyText: null }, { reaction: '✅' },
    { groupId: otherGroup }, { recipient: '111@s.whatsapp.net' }, { handled: 'true' }, { duplicate: 0 }, { reaction: {} } ])
    assert.throws(() => validateCallback({ ...valid, ...changed }), /callback_invalid/);
  assert.deepEqual(validateCallback(valid), { ...valid, replyPrivately: false });
});

test('Callback HTTP response accepts 16 KiB UTF8 and escaped text but rejects overflow', async () => {
  const valid = { handled: true, duplicate: false, reaction: null, replyText: 'è'.repeat(2000), replyOperationId: randomUUID() };
  const body = JSON.stringify(valid).replaceAll('è', '\\u00e8');
  assert.ok(Buffer.byteLength(body) > 4096); assert.ok(Buffer.byteLength(body) < 16384);
  const callback = data => createCallback('http://127.0.0.1:5238/internal/whatsapp/incoming', token, async () => new Response(data));
  assert.equal((await callback(body)({})).replyText, valid.replyText);
  await assert.rejects(callback(' '.repeat(16385))({}), /callback_invalid/);
  await assert.rejects(callback(JSON.stringify({ ...valid, groupId: otherGroup }))({}), /callback_invalid/);
});

const privateMessage = (id, text, overrides = {}) => incoming(id, {
  key: { remoteJid: '111:4@lid', remoteJidAlt: '222@s.whatsapp.net', participant: '999@lid', id, fromMe: false },
  message: { conversation: text }, ...overrides
});

test('Proponi opens only a private reply to the verified group author and permits a group checkmark', async t => {
  const h = await harness(t, { callback: async () => ({ handled: true, duplicate: false, reaction: '✅',
    replyText: 'Quale pasto?', replyOperationId: randomUUID(), replyPrivately: true }) });
  await h.ready(); const socket = h.sockets[0];
  socket.handlers.messages({ type: 'notify', messages: [incoming('proposal', { message: { conversation: '!proponi' } })] });
  await h.gateway.idle();
  assert.deepEqual(socket.privateSent, [['111@lid', 'Quale pasto?']]);
  assert.equal(socket.sent.length, 0); assert.equal(socket.reactions.length, 1);
  socket.handlers.messages({ type: 'notify', messages: [incoming('help-private', { message: { conversation: '!aiuto' } })] });
  await h.gateway.idle(); assert.equal(socket.privateSent.length, 1); assert.equal(socket.reactions.length, 1);
});

test('Preferenze replies privately only to the verified group author and suppresses duplicate starts', async t => {
  const payloads = []; const operationId = randomUUID();
  const h = await harness(t, { callback: async payload => {
    payloads.push(payload);
    return { handled: true, duplicate: false, reaction: '✅', replyText: 'Scegli il pasto',
      replyOperationId: operationId, replyPrivately: true };
  } });
  await h.ready(); const socket = h.sockets[0];
  const message = incoming('preferences-start', { message: { conversation: '!preferenze' } });
  socket.handlers.messages({ type: 'notify', messages: [message, message] });
  await h.gateway.idle();
  assert.equal(payloads.length, 1); assert.equal(payloads[0].isPrivate, false);
  assert.deepEqual(payloads[0].senderIds, ['111@lid', '222@s.whatsapp.net']);
  assert.deepEqual(socket.privateSent, [['111@lid', 'Scegli il pasto']]);
  assert.equal(socket.sent.length, 0); assert.equal(socket.reactions.length, 1);
  socket.handlers.messages({ type: 'notify', messages: [incoming('preferences-retry', {
    message: { conversation: '  !PREFERENZE  ' }
  })] });
  await h.gateway.idle();
  assert.equal(payloads.length, 2); assert.equal(socket.privateSent.length, 1);
});

test('Preferenze retains exact command, group, envelope, timestamp and sender filters', async t => {
  const h = await harness(t); await h.ready(); const socket = h.sockets[0];
  const message = incoming('preferences', { message: { conversation: '!preferenze' } });
  const messages = [
    ...['!preferenze 1', '!preferenze2', '!preferenze\n1', 'preferenze'].map((text, i) =>
      incoming('preferences-invalid-' + i, { message: { conversation: text } })),
    { ...message, key: { ...message.key, id: 'other-group', remoteJid: otherGroup } },
    { ...message, key: { ...message.key, id: 'broadcast', remoteJid: 'status@broadcast' } },
    { ...message, key: { ...message.key, id: 'self', fromMe: true } },
    { ...message, key: { ...message.key, id: 'no-sender', participant: null, participantAlt: null } },
    { ...message, key: { ...message.key, id: 'invalid-sender', participant: 'invalid', participantAlt: null } },
    { ...message, messageTimestamp: now / 1000 - 121 },
    { ...message, message: { imageMessage: { caption: '!preferenze' } } }
  ];
  socket.handlers.messages({ type: 'append', messages: [message] });
  socket.handlers.messages({ type: 'notify', messages });
  await h.gateway.idle();
  assert.equal(h.callbacks.length, 0); assert.equal(socket.privateSent.length, 0);
  assert.equal(socket.sent.length, 0); assert.equal(socket.reactions.length, 0);
});

test('Private replies remain forbidden for other group commands and cannot choose an arbitrary recipient', async t => {
  const h = await harness(t, { callback: async () => ({ handled: true, duplicate: false, reaction: '✅',
    replyText: 'Riservato', replyOperationId: randomUUID(), replyPrivately: true }) });
  await h.ready(); const socket = h.sockets[0];
  socket.handlers.messages({ type: 'notify', messages: ['!aiuto', '!presenza', '!menu', '!todo', '!conferma', '!annulla']
    .map((text, i) => incoming('forbidden-private-' + i, { message: { conversation: text } })) });
  await h.gateway.idle();
  assert.equal(h.gateway.health().diagnostics.callbackFailed, 6);
  assert.equal(socket.privateSent.length, 0); assert.equal(socket.sent.length, 0); assert.equal(socket.reactions.length, 0);
  h.setTime(now + 60000);
  h.gateway.callback = async () => ({ handled: true, duplicate: false, replyText: 'Riservato',
    replyOperationId: randomUUID(), replyPrivately: true, recipient: '999@lid' });
  socket.handlers.messages({ type: 'notify', messages: [incoming('spoof-preferences', {
    messageTimestamp: (now + 60000) / 1000, message: { conversation: '!preferenze' }
  })] });
  await h.gateway.idle();
  assert.equal(h.gateway.health().diagnostics.callbackFailed, 7); assert.equal(socket.privateSent.length, 0);
  const server = createGatewayServer(h.gateway, token);
  await new Promise(resolve => server.listen(0, '127.0.0.1', resolve));
  t.after(() => new Promise(resolve => server.close(resolve)));
  for (const body of [
    { operationId: randomUUID(), groupId: '999@lid', text: 'Forbidden' },
    { operationId: randomUUID(), groupId: group, text: 'Forbidden', recipient: '999@lid' },
    { operationId: randomUUID(), groupId: group, text: 'Forbidden', replyPrivately: true }
  ]) assert.equal((await request(server, '/send', { method: 'POST', body })).status, 400);
  assert.equal(socket.sent.length, 0); assert.equal(socket.privateSent.length, 0);
});

test('Preferences duplicate identities remain isolated between group and private channels', async t => {
  const h = await harness(t, { callback: async payload => {
    h.callbacks.push(payload);
    return { handled: false, duplicate: false, reaction: null };
  } });
  await h.ready(); const socket = h.sockets[0];
  const start = incoming('same-channel-id', { message: { conversation: '!preferenze' } });
  const privateInput = privateMessage('same-channel-id', '!preferenze');
  socket.handlers.messages({ type: 'notify', messages: [start, start, privateInput, privateInput] });
  await h.gateway.idle();
  assert.deepEqual(h.callbacks.map(payload => payload.isPrivate), [false, true]);
  assert.equal(socket.privateSent.length, 0); assert.equal(socket.sent.length, 0); assert.equal(socket.reactions.length, 0);
});

test('Private plain input uses remote identity only, active group, bounded payload and no public fallback', async t => {
  const h = await harness(t); await h.ready(); const socket = h.sockets[0];
  socket.handlers.messages({ type: 'notify', messages: [privateMessage('private', 'Pasta al forno'),
    privateMessage('private', 'Pasta al forno'), privateMessage('long-private', 'x'.repeat(301)),
    privateMessage('media-private', 'ignored', { message: { imageMessage: { caption: 'Pasta' } } }),
    privateMessage('self-private', 'Pasta', { key: { ...privateMessage('self-private', '').key, fromMe: true } })] });
  await h.gateway.idle(); assert.equal(h.callbacks.length, 1);
  assert.deepEqual(h.callbacks[0].senderIds, ['111@lid', '222@s.whatsapp.net']);
  assert.equal(h.callbacks[0].groupId, group); assert.equal(h.callbacks[0].isPrivate, true);
  assert.equal(h.callbacks[0].text, 'Pasta al forno'); assert.equal(socket.reactions.length, 0);
  h.gateway.callback = async () => ({ handled: true, duplicate: false, replyText: 'Riservato', replyOperationId: randomUUID() });
  socket.handlers.messages({ type: 'notify', messages: [privateMessage('no-fallback', '!annulla')] });
  await h.gateway.idle(); assert.equal(socket.sent.length, 0); assert.equal(socket.privateSent.length, 0);
});

test('Private dialogs allow corrections within a separate fifteen-per-minute budget and retain the group budget', async t => {
  const h = await harness(t); await h.ready(); const socket = h.sockets[0];
  socket.handlers.messages({ type: 'notify', messages: [incoming('start-dialog', { message: { conversation: '!proponi' } }),
    ...Array.from({ length: 16 }, (_, index) => privateMessage('dialog-' + index, index === 6 ? '!annulla' : 'Risposta ' + index)),
    ...Array.from({ length: 6 }, (_, index) => incoming('group-budget-' + index))] });
  await h.gateway.idle();
  assert.equal(h.callbacks.filter(payload => payload.isPrivate).length, 15);
  assert.equal(h.callbacks.filter(payload => !payload.isPrivate).length, 6);
  assert.ok(h.callbacks.some(payload => payload.text === '!annulla'));
  assert.equal(h.gateway.health().diagnostics.rateRejected, 2);
  h.setTime(now + 60000);
  socket.handlers.messages({ type: 'notify', messages: [privateMessage('next-minute', 'Conferma', { messageTimestamp: (now + 60000) / 1000 })] });
  await h.gateway.idle(); assert.equal(h.callbacks.filter(payload => payload.isPrivate).length, 16);
});

test('A failed private send remains uncertain, never falls back to group and never resends', async t => {
  const operationId = randomUUID();
  const h = await harness(t, { callback: async () => ({ handled: true, duplicate: false, replyText: 'Riservato',
    replyOperationId: operationId, replyPrivately: true }) });
  await h.ready(); const socket = h.sockets[0]; let attempts = 0;
  socket.sendPrivate = async () => { attempts++; throw new Error('synthetic_private_failure'); };
  for (const id of ['failed-private', 'retry-private']) {
    socket.handlers.messages({ type: 'notify', messages: [privateMessage(id, 'Pasta')] }); await h.gateway.idle();
  }
  assert.equal(attempts, 1); assert.equal(socket.sent.length, 0); assert.equal(socket.reactions.length, 0);
  assert.equal((await h.journal.get(operationId, h.journal.fingerprint(group + ':private:111@lid', 'Riservato'))).status, 'uncertain');
});

test('Private callback journal binds recipient, never replays and keeps private text out of durable files', async t => {
  const operationId = randomUUID(); const h = await harness(t, { callback: async () => ({ handled: true, duplicate: false,
    replyText: 'Pasto riservato', replyOperationId: operationId, replyPrivately: true }) });
  await h.ready(); const socket = h.sockets[0];
  for (const id of ['first-private', 'second-private']) {
    socket.handlers.messages({ type: 'notify', messages: [privateMessage(id, '!annulla')] }); await h.gateway.idle();
  }
  assert.equal(socket.privateSent.length, 1);
  socket.handlers.messages({ type: 'notify', messages: [privateMessage('different-recipient', 'Pasta', {
    key: { remoteJid: '333@lid', id: 'different-recipient' } })] });
  await h.gateway.idle(); assert.equal(socket.privateSent.length, 1); assert.equal(socket.sent.length, 0);
  for (const file of await readdir(h.directory)) {
    const stored = await readFile(join(h.directory, file), 'utf8');
    for (const value of ['Pasto riservato', '111@lid', group]) assert.ok(!stored.includes(value));
  }
  await assert.rejects(h.gateway.send({ operationId: randomUUID(), groupId: '111@lid', text: 'Forbidden' }), /invalid_send/);
});

for (const privateReply of [false, true]) {
for (const boundary of ['callback', 'sendTail', 'journal-get', 'journal-pending']) {
  for (const change of ['repin', 'reconnect']) {
    test(`${privateReply ? 'Private' : 'Group'} reply preserves original connection through ${boundary} across ${change}`, async t => {
      const operationId = randomUUID(); const reply = { handled: true, duplicate: false, reaction: null,
        replyText: 'Risposta sintetica', replyOperationId: operationId, replyPrivately: privateReply };
      let release; let entered;
      const gate = new Promise(resolve => { release = resolve; });
      const started = new Promise(resolve => { entered = resolve; });
      const h = await harness(t); await h.ready(); const originalSocket = h.sockets[0];
      h.gateway.callback = async () => { if (boundary === 'callback') { entered(); await gate; } return reply; };
      if (boundary === 'sendTail') h.gateway.sendTail = Promise.resolve().then(async () => { entered(); await gate; });
      if (boundary === 'journal-get' || boundary === 'journal-pending') {
        const method = boundary === 'journal-get' ? 'get' : 'pending'; const original = h.journal[method].bind(h.journal);
        h.journal[method] = async (...args) => { const result = await original(...args); entered(); await gate; return result; };
      }
      originalSocket.handlers.messages({ type: 'notify', messages: [privateReply ? privateMessage('fenced-private', 'Pasta')
        : incoming('fenced-info', { message: { conversation: '!aiuto' } })] });
      await started;
      if (change === 'repin') await h.gateway.group(group);
      else { h.gateway.disconnect(); await h.ready(); }
      release(); await h.gateway.idle();
      assert.ok(h.sockets.every(socket => socket.sent.length === 0 && socket.privateSent.length === 0 && socket.reactions.length === 0));
      // A fenced or uncertain operation is never replayed automatically after restart.
      if (boundary !== 'callback' && change === 'repin') assert.equal((await h.journal.get(operationId,
        h.journal.fingerprint(privateReply ? group + ':private:111@lid' : group, reply.replyText))).status, 'uncertain');
    });
  }
}
}
