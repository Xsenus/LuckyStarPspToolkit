import test from 'node:test';
import assert from 'node:assert/strict';
import { webcrypto } from 'node:crypto';
import { makeKey, integer, issueRequest, reserveStatus, pageItems, requestApi, mutationApi, clearPendingMutations } from '../src/model.mjs';
test('keys have 256-bit canonical format and differ', () => { const keys = new Set(Array.from({ length: 100 }, () => makeKey(webcrypto))); assert.equal(keys.size, 100); for (const key of keys)
    assert.match(key, /^LSP-[A-Za-z0-9_-]{43}$/); });
test('bounded integral amounts reject injection and fractions', () => { for (const v of ['1x', 0, -1, 1.2, Infinity])
    assert.throws(() => integer(v, 1, 168)); assert.equal(integer('168', 1, 168), 168); assert.throws(() => integer(169, 1, 168)); });
test('permanent issue never invents an expiry', () => { const x = issueRequest({ unit: 'permanent', starts: 'activation', devices: 2, label: 'Owner' }, webcrypto); assert.equal(x.amount, 0); assert.equal(x.maxDevices, 2); assert.ok(x.id); });
test('invalid issue parameters fail locally', () => { for (const values of [{ unit: 'months', starts: 'issue' }, { unit: 'days', starts: 'unknown' }, { unit: 'days', starts: 'issue', amount: 1, devices: 0 }])
    assert.throws(() => issueRequest(values, webcrypto)); });
test('epoch retirement is not represented as an active reserve', () => { const g = { epoch: 1, revokedAt: null }; assert.equal(reserveStatus(g, { epoch: 2, enabled: true }), 'Старое поколение'); assert.equal(reserveStatus(g, { epoch: 1, enabled: false }), 'Отключён'); assert.equal(reserveStatus({ ...g, revokedAt: 5 }, { epoch: 1, enabled: true }), 'Отозван'); });
test('page slices do not mutate input', () => { const data = [1, 2, 3, 4]; assert.deepEqual(pageItems(data, 1, 2), [3, 4]); assert.deepEqual(data, [1, 2, 3, 4]); });
test('API sends credentials and CSRF only same-origin', async () => { let seen; await requestApi('/issue', { id: 'x' }, 'csrf', async (url, options) => { seen = { url, options }; return new Response('{"ok":true}', { headers: { 'content-type': 'application/json' } }); }); assert.equal(seen.url, '/api/issue'); assert.equal(seen.options.credentials, 'same-origin'); assert.equal(seen.options.headers['X-CSRF-Token'], 'csrf'); });
test('API propagates explicit authorization denial', async () => { await assert.rejects(requestApi('/session', undefined, undefined, async () => new Response('{"code":"WEB_UNAUTHORIZED","message":"Denied"}', { status: 401, headers: { 'content-type': 'application/json' } })), e => e.code === 'WEB_UNAUTHORIZED'); });
test('HTML proxy errors do not appear as success', async () => { await assert.rejects(requestApi('/session', undefined, undefined, async () => new Response('<html>bad</html>', { status: 502, headers: { 'content-type': 'text/html' } }))); });

test('ambiguous mutation retry keeps the same request ID; confirmed repeats use a new one', async () => {
    clearPendingMutations(); const observed = []; let fail = true;
    const sender = async (_path, body) => { observed.push(body.requestId); if (fail) { fail = false; throw new Error('reply lost'); } return {ok: true}; };
    await assert.rejects(mutationApi('/change', {requestId: 'first', licenseId: 'L', action:'extend', amount:1}, 'csrf', sender));
    await mutationApi('/change', {requestId: 'second', licenseId: 'L', action:'extend', amount:1}, 'csrf', sender);
    await mutationApi('/change', {requestId: 'third', licenseId: 'L', action:'extend', amount:1}, 'csrf', sender);
    assert.deepEqual(observed, ['first', 'first', 'third']); clearPendingMutations();
});
