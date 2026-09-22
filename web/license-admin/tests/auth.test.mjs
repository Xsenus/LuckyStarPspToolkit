import test from 'node:test';
import assert from 'node:assert/strict';
import { ownerAuthMessage } from '../src/model.mjs';

test('busy verification prompts an explicit retry, never repeats credentials', () => {
    assert.match(ownerAuthMessage({ code: 'WEB_BUSY' }), /через секунду/);
    assert.match(ownerAuthMessage({ code: 'WEB_BUSY' }), /сессии не закрыты/);
});
test('login and recent-MFA throttling explain independent scopes', () => {
    assert.match(ownerAuthMessage({ code: 'WEB_RATE_LIMIT' }), /с этого адреса/);
    assert.match(ownerAuthMessage({ code: 'WEB_RATE_LIMIT' }, true), /этой сессии/);
    assert.match(ownerAuthMessage({ code: 'WEB_RATE_LIMIT' }, true), /Смена адреса не сбрасывает/);
});
test('invalid proof remains a generic factor failure without identifying the correct factor', () => {
    assert.match(ownerAuthMessage({ code: 'WEB_AUTH_FAILED' }), /логин, пароль или/);
});
test('expired/revoked session cannot be presented as successful recent MFA', () => {
    assert.match(ownerAuthMessage({ code: 'WEB_UNAUTHORIZED' }, true), /войдите заново/);
});
test('server shutdown requires a new login, never cached permission', () => {
    assert.match(ownerAuthMessage({ code: 'WEB_CLOSED' }), /новый вход/);
});
test('recent-MFA deadline explanation requires a new one-time code', () => {
    assert.match(ownerAuthMessage({ code: 'WEB_REAUTH_REQUIRED' }, true), /новый одноразовый код/);
});
test('network timeout warns that outcome is unknown and does not replay a code', () => {
    assert.match(ownerAuthMessage({ name: 'AbortError' }), /не получен/);
    assert.match(ownerAuthMessage({ name: 'AbortError' }), /свежий/);
});
test('unknown errors do not reflect a secret-bearing exception message', () => {
    assert.doesNotMatch(ownerAuthMessage({ message: 'PRIVATE-TEST-SENTINEL' }), /PRIVATE-TEST/);
    assert.equal(ownerAuthMessage(null), ownerAuthMessage(undefined));
});
