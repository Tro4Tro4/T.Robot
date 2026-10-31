import test from 'node:test';
import assert from 'node:assert/strict';
import { EventEmitter } from 'node:events';
import { createBaileysTransport } from '../src/transport.js';

test('Baileys adapter opts out of history, broadcasts, online status and caches; verified metadata aliases only', async () => {
  const events = new EventEmitter(); const updates = []; const batches = []; const sends = [];
  let options; let ended = 0; let saved = 0; let authPath;
  const socket = { ev: events, end: async () => { ended++; },
    groupFetchAllParticipating: async () => ({ one: { id: '12345@g.us', subject: 'Sintetico' }, bad: { id: '222@s.whatsapp.net', subject: 'DM escluso' } }),
    groupMetadata: async () => ({ participants: [
      { id: '111:2@lid', lid: '111@lid', phoneNumber: '222:4@s.whatsapp.net' },
      { id: '333@lid' },
      { id: 'status@broadcast' }
    ] }),
    sendMessage: async (...args) => { sends.push(args); return { key: { id: 'technical' } }; }
  };
  const library = { useMultiFileAuthState: async path => { authPath = path; return { state: { creds: {}, keys: {} }, saveCreds: async () => { saved++; } }; },
    makeCacheableSignalKeyStore: keys => keys, makeWASocket: input => { options = input; return socket; },
    Browsers: { windows: name => ['Windows', name, 'test'] }, logger: { level: 'silent' },
    jidNormalizedUser: id => id.replace(/:\d+@/, '@'), DisconnectReason: { loggedOut: 401 },
    qrcode: { toDataURL: async () => 'data:image/png;base64,synthetic' }
  };
  const transport = await createBaileysTransport('synthetic-state', { update: update => updates.push(update), messages: batch => batches.push(batch) }, library);
  assert.ok(authPath.endsWith('auth')); assert.equal(options.syncFullHistory, false);
  assert.equal(options.shouldSyncHistoryMessage({}), false); assert.equal(options.markOnlineOnConnect, false);
  assert.equal(options.enableRecentMessageCache, false); assert.equal(options.emitOwnEvents, false);
  assert.equal(options.shouldIgnoreJid('222@s.whatsapp.net'), false); assert.equal(options.shouldIgnoreJid('12345@g.us'), false);
  assert.equal(options.shouldIgnoreJid('status@broadcast'), true);
  assert.deepEqual((await transport.groups()).map(g => g.id), ['12345@g.us']);
  assert.deepEqual(await transport.members('12345@g.us'), [
    { id: '111@lid', alternateIds: ['222@s.whatsapp.net'], label: 'Numero WhatsApp 222' },
    { id: '333@lid', alternateIds: [], label: 'Identità WhatsApp 333@lid' }
  ]);
  events.emit('messages.upsert', { type: 'append', messages: [] });
  events.emit('messages.upsert', { type: 'notify', messages: [] });
  assert.equal(batches.length, 1);
  events.emit('connection.update', { qr: 'never-print-this' }); await Promise.resolve();
  assert.equal(updates.at(-1).qrDataUrl, 'data:image/png;base64,synthetic');
  events.emit('creds.update', {}); await Promise.resolve();
  assert.equal(await transport.send('12345@g.us', 'Testo sintetico'), 'technical');
  assert.deepEqual(sends[0], ['12345@g.us', { text: 'Testo sintetico', linkPreview: null }]);
  await transport.react('12345@g.us', { remoteJid: '12345@g.us', id: 'technical' }, 'private text');
  assert.equal(sends.length, 1);
  await assert.rejects(transport.sendPrivate('12345@g.us', 'Riservato'), /private_send_invalid/);
  await assert.rejects(transport.sendPrivate('111:2@lid', 'Riservato'), /private_send_invalid/);
  assert.equal(await transport.sendPrivate('111@lid', 'Riservato'), 'technical');
  assert.deepEqual(sends[1], ['111@lid', { text: 'Riservato', linkPreview: null }]);
  await transport.close(); assert.equal(ended, 1); assert.equal(saved, 1);
  assert.equal(events.listenerCount('messages.upsert'), 0); assert.equal(events.listenerCount('creds.update'), 0);
  await assert.rejects(transport.send('12345@g.us', 'Chiuso'), /transport_closed/);
});

test('QR completed after open/close cannot restore an obsolete QR', async () => {
  const events = new EventEmitter(); const updates = []; let finishQr;
  const transport = await createBaileysTransport('synthetic', { update: update => updates.push(update), messages: () => {} }, {
    useMultiFileAuthState: async () => ({ state: { creds: {}, keys: {} }, saveCreds: async () => {} }),
    makeCacheableSignalKeyStore: x => x, makeWASocket: () => ({ ev: events, end: async () => {} }),
    logger: {}, Browsers: { windows: () => [] }, DisconnectReason: { loggedOut: 401 },
    qrcode: { toDataURL: () => new Promise(resolve => { finishQr = resolve; }) }
  });
  events.emit('connection.update', { qr: 'synthetic' });
  events.emit('connection.update', { connection: 'open' });
  finishQr('data:image/png;base64,obsolete'); await Promise.resolve();
  assert.equal(updates.length, 1); assert.equal(updates[0].connection, 'open');
  await transport.close();
});
