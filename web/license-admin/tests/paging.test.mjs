import test from 'node:test';
import assert from 'node:assert/strict';
import { createOwnerPager } from '../src/model.mjs';

/** Construct a small valid response without pretending to be a live licensing server. */
function page(ids = ['a'], nextCursor = '', total = ids.length) {
    return { items: ids.map(id => ({ id })), total, nextCursor, revision: 3, asOfUtc: 100 };
}
/** Explicit externally controlled promise for ordering and cancellation tests. */
function deferred() { let resolve, reject; const promise = new Promise((a, b) => { resolve = a; reject = b; }); return { promise, resolve, reject }; }

test('query is bounded, authenticated and includes filters without a URL query string', async () => {
    const calls = []; const pager = createOwnerPager('/licenses/query', 'csrf', async (...x) => { calls.push(x); return page(); });
    await pager.load({ search: 'Клиент', status: 'active' });
    assert.deepEqual(calls[0], ['/licenses/query', { limit: 25, search: 'Клиент', status: 'active', activatedOnly: false, cursor: '' }, 'csrf']);
});
test('next and previous use server tokens and never cache the prior page rows', async () => {
    const cursors = []; const pager = createOwnerPager('/licenses/query', 'c', async (_, q) => { cursors.push(q.cursor); return q.cursor ? page(['b'], '', 2) : page(['a'], 'next', 2); });
    await pager.load(); await pager.load({}, 'next'); assert.equal(pager.state.index, 1); assert.equal(pager.state.data.items[0].id, 'b');
    await pager.load({}, 'previous'); assert.equal(pager.state.index, 0); assert.deepEqual(cursors, ['', 'next', '']);
});
test('failed next keeps complete old state and explicit retry uses the same cursor', async () => {
    let fail = true; const pager = createOwnerPager('/licenses/query', 'c', async (_, q) => {
        if (q.cursor && fail) throw new Error('network'); return q.cursor ? page(['b'], '', 2) : page(['a'], 'next', 2);
    });
    await pager.load(); const prior = pager.state;
    await assert.rejects(pager.load({}, 'next'), /network/); assert.equal(pager.state, prior);
    fail = false; await pager.load({}, 'next'); assert.equal(pager.state.index, 1);
});
test('stale continuation refreshes first page exactly once and discloses reset', async () => {
    const calls = []; const pager = createOwnerPager('/licenses/query', 'c', async (_, q) => {
        calls.push(q.cursor); if (q.cursor) throw Object.assign(new Error('stale'), { code: 'OWNER_PAGE_STALE' }); return page(['a'], 'next', 2);
    });
    await pager.load(); await pager.load({}, 'next'); assert.deepEqual(calls, ['', 'next', '']); assert.equal(pager.state.index, 0);
    assert.match(pager.state.notice, /первая страница/);
});
test('a failed first-page refresh is not hidden by a retry loop', async () => {
    let count = 0; const pager = createOwnerPager('/licenses/query', 'c', async () => { count++; throw Object.assign(new Error('invalid'), { code: 'OWNER_PAGE_CURSOR' }); });
    await assert.rejects(pager.load()); assert.equal(count, 1);
});
test('malformed or oversized pages never commit navigation', async () => {
    for (const invalid of [page(Array.from({ length: 26 }, (_, i) => String(i))), page(['a', 'a']), page([], 'next'), { ...page(), total: -1 }, { ...page(), nextCursor: 'x'.repeat(2049) }]) {
        const pager = createOwnerPager('/licenses/query', 'c', async () => invalid); const initial = pager.state;
        await assert.rejects(pager.load()); assert.equal(pager.state, initial);
    }
});
test('superseded query response cannot overwrite a newer search', async () => {
    const old = deferred(); const pager = createOwnerPager('/licenses/query', 'c', async (_, q) => q.search === 'old' ? old.promise : page(['new']));
    const a = pager.load({ search: 'old' }); await pager.load({ search: 'new' }); old.resolve(page(['old'])); await a;
    assert.equal(pager.state.data.items[0].id, 'new'); assert.equal(pager.state.query.search, 'new');
});
test('superseded stale failure cannot trigger a first-page query in the new session', async () => {
    const old = deferred(); let count = 0; const pager = createOwnerPager('/licenses/query', 'c', async (_, q) => { count++; return q.search === 'old' ? old.promise : page(['new']); });
    const a = pager.load({ search: 'old' }); await pager.load({ search: 'new' }); old.reject(Object.assign(new Error('old'), { code: 'OWNER_PAGE_STALE' })); await a;
    assert.equal(count, 2); assert.equal(pager.state.data.items[0].id, 'new');
});
test('unmount cancellation prevents late state commit', async () => {
    const pending = deferred(); const pager = createOwnerPager('/licenses/query', 'c', async () => pending.promise); const initial = pager.state;
    const a = pager.load(); pager.cancel(); pending.resolve(page()); await a; assert.equal(pager.state, initial);
});
test('filters must restart pagination and directions at boundaries send no request', async () => {
    let count = 0; const pager = createOwnerPager('/licenses/query', 'c', async () => { count++; return page(); });
    await pager.load(); await pager.load({}, 'next'); await pager.load({}, 'previous'); assert.equal(count, 1);
    await assert.rejects(pager.load({ search: 'changed' }, 'next')); await assert.rejects(pager.load({}, 'random'));
});
