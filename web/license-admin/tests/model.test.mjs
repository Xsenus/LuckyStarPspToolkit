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

test('concurrent mutation clicks coalesce one HTTP operation', async () => {
    clearPendingMutations(); let finish; let calls = 0;
    const sender = () => { calls++; return new Promise(resolve => { finish = resolve; }); };
    const a = mutationApi('/change', {requestId: 'a', action:'extend'}, 'csrf', sender);
    const b = mutationApi('/change', {requestId: 'b', action:'extend'}, 'csrf', sender);
    await Promise.resolve(); assert.equal(calls, 1); finish({ok:true});
    assert.deepEqual(await a, await b); clearPendingMutations();
});
test('failed mutation snapshots are isolated from caller edits and property order', async () => {
    clearPendingMutations(); const seen = []; let first = true;
    const sender = async (_p, body) => { seen.push(body); if (first) {first=false; throw Error('lost');} return {}; };
    const body = {requestId:'original', action:'extend', extra:{amount:1}};
    await assert.rejects(mutationApi('/change', body, 'csrf', sender)); body.extra.amount = 9;
    await mutationApi('/change', {extra:{amount:1}, action:'extend', requestId:'new'}, 'csrf', sender);
    assert.equal(seen[1].requestId, 'original'); assert.equal(seen[1].extra.amount, 1); clearPendingMutations();
});
test('late reply from a signed-out context cannot delete a new retry identity', async () => {
    clearPendingMutations(); let finish;
    const old = mutationApi('/change', {requestId:'old', action:'extend'}, 'old-csrf', () => new Promise(resolve => { finish=resolve; }));
    await Promise.resolve(); clearPendingMutations();
    await assert.rejects(mutationApi('/change', {requestId:'new', action:'extend'}, 'new-csrf', async () => { throw Error('lost new response'); }));
    finish({ok:true}); await old;
    let actual;
    await mutationApi('/change', {requestId:'wrong', action:'extend'}, 'new-csrf', async (_p, data) => { actual=data.requestId; return {}; });
    assert.equal(actual, 'new'); clearPendingMutations();
});
test('response reader enforces byte budget before buffering the remainder', async () => {
    const {readJsonBounded} = await import('../src/model.mjs'); let canceled = false;
    const body = new ReadableStream({start(c){ c.enqueue(new Uint8Array(9)); }, cancel(){canceled=true;}});
    await assert.rejects(readJsonBounded(new Response(body), 8)); assert.equal(canceled, true);
});
test('declared oversize body is cancelled before being read', async () => {
    const {readJsonBounded} = await import('../src/model.mjs'); let canceled = false;
    const body = new ReadableStream({cancel(){canceled=true;}});
    await assert.rejects(readJsonBounded(new Response(body,{headers:{'content-length':'1000'}}),8)); assert.equal(canceled,true);
});
test('UTF8 split across chunks remains valid and counts bytes instead of characters', async () => {
    const {readJsonBounded} = await import('../src/model.mjs'); const bytes = new TextEncoder().encode('{"s":"Я"}');
    const stream = () => new ReadableStream({start(c){ for(const b of bytes)c.enqueue(Uint8Array.of(b)); c.close(); }});
    assert.deepEqual(await readJsonBounded(new Response(stream()),bytes.length),{s:'Я'});
    await assert.rejects(readJsonBounded(new Response(stream()),bytes.length-1));
});
test('invalid UTF8 cannot silently replace bytes in JSON', async () => {
    const {readJsonBounded} = await import('../src/model.mjs');
    await assert.rejects(readJsonBounded(new Response(Uint8Array.of(34,255,34)), 10));
});
test('null error response has a controlled message', async () => {
    await assert.rejects(requestApi('/session', undefined, undefined, async()=>new Response('null',{status:403,headers:{'content-type':'application/json'}})),e=>e.message==='Операция отклонена');
});
