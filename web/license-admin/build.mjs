/** Reproducible browser build. Verifies pinned MIT runtime files; never evaluates code or downloads dependencies. */
import { readFileSync, writeFileSync, mkdirSync, existsSync, renameSync, rmSync, copyFileSync } from 'node:fs';
import { createHash } from 'node:crypto';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
const root = path.dirname(fileURLToPath(import.meta.url));
if (Number(process.versions.node.split('.')[0]) < 22) throw new Error('Node.js 22 or newer is required');
const packageVersion = JSON.parse(readFileSync(path.join(root, 'package.json'))).version;
if (packageVersion !== readFileSync(path.join(root, '../../VERSION'), 'utf8').trim()) throw new Error('Frontend/product versions differ');
const manifest = JSON.parse(readFileSync(path.join(root, 'vendor-manifest.json'), 'utf8'));
const hash = bytes => createHash('sha256').update(bytes).digest('hex');
const modules = [];
for (const file of manifest.files) {
    const source = readFileSync(path.join(root, file.path));
    if (hash(source) !== file.sha256)
        throw new Error('Vendor integrity mismatch: ' + file.path);
    const requires = [...source.toString().matchAll(/require\(["']([^"']+)["']\)/g)].map(m => m[1]);
    if (requires.some(m => !manifest.files.some(f => f.module === m)))
        throw new Error('Unexpected vendor dependency');
    modules.push(`${JSON.stringify(file.module)}: function(module,exports,require){\n${source}\n}`);
}
const vendor = `/* React MIT. Exact build provenance: vendor-manifest.json. */\nconst definitions={${modules.join(',\n')}};\nconst cache=Object.create(null);\nfunction require(id){if(cache[id])return cache[id].exports;if(!Object.hasOwn(definitions,id))throw new Error('Unknown module');const m={exports:{}};cache[id]=m;definitions[id](m,m.exports,require);return m.exports;}\nexport const React=require('react');\nexport const createRoot=require('react-dom/client').createRoot;\n`;
const app = readFileSync(path.join(root, 'src/model.mjs'), 'utf8').replace(/^export /gm, '') + '\n' + readFileSync(path.join(root, 'src/app.mjs'), 'utf8').replace(/^import .*from '\.\/model\.mjs';\n/m, '');
const stage = path.join(root, '.dist-stage');
const dest = path.join(root, 'dist');
const backup = path.join(root, '.dist-old');
if (existsSync(stage) || existsSync(backup))
    throw new Error('Previous staging directory exists; inspect before removing.');
mkdirSync(path.join(stage, 'assets'), { recursive: true });
try {
    writeFileSync(path.join(stage, 'assets/vendor.js'), vendor);
    writeFileSync(path.join(stage, 'assets/app.js'), app);
    copyFileSync(path.join(root, 'src/app.css'), path.join(stage, 'assets/app.css'));
    copyFileSync(path.join(root, 'index.html'), path.join(stage, 'index.html'));
    copyFileSync(path.join(root, 'vendor/react/LICENSE.txt'), path.join(stage, 'THIRD_PARTY_LICENSES.txt'));
    copyFileSync(path.join(root, 'vendor-manifest.json'), path.join(stage, 'VENDOR-PROVENANCE.json'));
    const files = ['assets/vendor.js', 'assets/app.js', 'assets/app.css', 'index.html', 'THIRD_PARTY_LICENSES.txt', 'VENDOR-PROVENANCE.json'];
    writeFileSync(path.join(stage, 'BUILD-MANIFEST.json'), JSON.stringify({ version: JSON.parse(readFileSync(path.join(root, 'package.json'))).version, react: manifest.reactVersion, sourceCommit: manifest.sourceCommit, files: files.map(name => ({ name, sha256: hash(readFileSync(path.join(stage, name))) })) }, null, 2) + '\n');
    if (existsSync(dest))
        renameSync(dest, backup);
    try {
        renameSync(stage, dest);
    }
    catch (e) {
        if (existsSync(backup))
            renameSync(backup, dest);
        throw e;
    }
}
catch (e) {
    rmSync(stage, { recursive: true, force: true });
    throw e;
}
// A cleanup error must never roll back an already committed build.
if (existsSync(backup))
    rmSync(backup, { recursive: true, force: true });
console.log('React admin build completed:', dest);
