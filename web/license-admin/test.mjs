/** Discover all owner-console tests without shell glob expansion, including on Windows PowerShell/cmd. */
import { readdirSync } from 'node:fs';
import { spawnSync } from 'node:child_process';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = path.dirname(fileURLToPath(import.meta.url));
const tests = readdirSync(path.join(root, 'tests'), { withFileTypes: true })
    .filter(file => file.isFile() && file.name.endsWith('.test.mjs'))
    .map(file => path.join(root, 'tests', file.name)).sort();
if (!tests.length) throw new Error('No owner-console tests discovered. Refusing an empty success.');
const result = spawnSync(process.execPath, ['--test', ...tests], { stdio: 'inherit', shell: false });
if (result.error) console.error(result.error.message);
process.exit(result.status ?? 1);
