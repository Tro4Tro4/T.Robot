import { createServer } from 'node:http';
import { request as httpsRequest } from 'node:https';
import { createSecureContext } from 'node:tls';
import { timingSafeEqual } from 'node:crypto';
import { GatewayError } from './journal.js';
import { validateCallback } from './gateway.js';

const json = (response, status, body) => {
  response.writeHead(status, { 'Content-Type': 'application/json; charset=utf-8', 'Cache-Control': 'no-store',
    'X-Content-Type-Options': 'nosniff' });
  response.end(JSON.stringify(body));
};
const equalBearer = (header, token) => {
  if (typeof header !== 'string') return false;
  const supplied = Buffer.from(header); const expected = Buffer.from('Bearer ' + token);
  return supplied.length === expected.length && timingSafeEqual(supplied, expected);
};
async function body(request) {
  if (!/^application\/json(?:\s*;.*)?$/i.test(request.headers['content-type'] ?? ''))
    throw new GatewayError('json_required', 415);
  let length = 0; const chunks = [];
  await new Promise((resolve, reject) => {
    let failed = false;
    request.on('data', chunk => {
      if (failed) return;
      length += chunk.length;
      if (length > 8192) { failed = true; chunks.length = 0; reject(new GatewayError('body_too_large', 413)); }
      else chunks.push(chunk);
    });
    request.once('end', resolve);
    request.once('error', () => reject(new GatewayError('invalid_body')));
    request.once('aborted', () => reject(new GatewayError('invalid_body')));
  });
  let parsed;
  try { parsed = JSON.parse(Buffer.concat(chunks).toString('utf8')); }
  catch { throw new GatewayError('invalid_json'); }
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) throw new GatewayError('invalid_json');
  return parsed;
}
const fields = (input, allowed, required = allowed) => {
  if (Object.keys(input).some(key => !allowed.includes(key)) || required.some(key => !(key in input)))
    throw new GatewayError('invalid_fields');
  return input;
};

export function createGatewayServer(gateway, token) {
  const server = createServer(async (request, response) => {
    try {
      const port = server.address()?.port;
      const validHosts = ['127.0.0.1:' + port, 'localhost:' + port, '[::1]:' + port];
      if (!['127.0.0.1', '::1', '::ffff:127.0.0.1'].includes(request.socket.remoteAddress)
        || !validHosts.includes(request.headers.host) || request.headers.origin)
        throw new GatewayError('local_requests_only', 403);
      if (!equalBearer(request.headers.authorization, token)) throw new GatewayError('unauthorized', 401);
      const path = request.url;
      if (request.method === 'GET' && path === '/health') return json(response, 200, gateway.health());
      if (request.method === 'GET' && path === '/groups') return json(response, 200, await gateway.groups());
      const members = /^\/groups\/([^/?]+)\/members$/.exec(path);
      if (request.method === 'GET' && members) {
        let id; try { id = decodeURIComponent(members[1]); } catch { throw new GatewayError('invalid_group'); }
        return json(response, 200, await gateway.members(id));
      }
      if (request.method === 'POST' && ['/connect', '/disconnect', '/group', '/send'].includes(path)) {
        const input = await body(request);
        if (path === '/connect') { fields(input, []); return json(response, 200, await gateway.connect()); }
        if (path === '/disconnect') { fields(input, []); return json(response, 200, gateway.disconnect()); }
        if (path === '/group') { fields(input, ['groupId']); return json(response, 200, await gateway.group(input.groupId)); }
        fields(input, ['operationId', 'groupId', 'text']);
        return json(response, 200, await gateway.send(input));
      }
      throw new GatewayError('not_found', 404);
    } catch (error) {
      json(response, error instanceof GatewayError ? error.status : 503,
        { error: error instanceof GatewayError ? error.code : 'gateway_unavailable' });
    }
  });
  server.requestTimeout = 15000; server.headersTimeout = 10000; server.keepAliveTimeout = 5000;
  server.maxConnections = 32; server.maxHeadersCount = 30;
  return server;
}

export function createCallback(callbackUrl, token, fetcher = fetch, tlsOptions) {
  if (callbackUrl === 'https://localhost:5443/internal/whatsapp/incoming') {
    if (!Buffer.isBuffer(tlsOptions?.ca) || !tlsOptions.ca.length)
      throw new Error('invalid_callback_configuration');
    // Parse the explicit CA at startup; TLS still checks chain, validity and the localhost identity per request.
    createSecureContext({ ca: tlsOptions.ca, minVersion: 'TLSv1.2' });
    const ca = Buffer.from(tlsOptions.ca);
    return payload => new Promise((resolve, reject) => {
      const fail = () => reject(new Error('callback_unavailable'));
      const request = httpsRequest({ hostname: '127.0.0.1', servername: 'localhost', port: 5443,
        path: '/internal/whatsapp/incoming', method: 'POST', ca, rejectUnauthorized: true,
        minVersion: 'TLSv1.2', agent: false, signal: AbortSignal.timeout(10000),
        headers: { Host: 'localhost:5443', Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' }
      }, response => {
        // https.request never follows redirects. Every non-success response is rejected.
        if (response.statusCode < 200 || response.statusCode >= 300) { response.destroy(); fail(); return; }
        let length = 0; const chunks = [];
        response.on('data', chunk => {
          length += chunk.length;
          if (length > 16384) { response.destroy(); reject(new Error('callback_invalid')); }
          else chunks.push(chunk);
        });
        response.once('error', fail);
        response.once('aborted', fail);
        response.once('end', () => {
          try { resolve(validateCallback(JSON.parse(Buffer.concat(chunks).toString('utf8')))); }
          catch { reject(new Error('callback_invalid')); }
        });
      });
      request.once('error', fail);
      request.end(JSON.stringify(payload));
    });
  }
  const url = new URL(callbackUrl);
  if (callbackUrl !== 'http://127.0.0.1:5238/internal/whatsapp/incoming')
    throw new Error('invalid_callback_configuration');
  return async payload => {
    const response = await fetcher(url, { method: 'POST', redirect: 'error',
      headers: { Authorization: 'Bearer ' + token, 'Content-Type': 'application/json' },
      body: JSON.stringify(payload), signal: AbortSignal.timeout(10000) });
    if (!response.ok) throw new Error('callback_unavailable');
    let length = 0; const chunks = [];
    for await (const chunk of response.body) {
      length += chunk.length;
      if (length > 16384) { throw new Error('callback_invalid'); }
      chunks.push(chunk);
    }
    const result = JSON.parse(Buffer.concat(chunks).toString('utf8'));
    return validateCallback(result);
  };
}
