import assert from 'node:assert/strict';
import { createHash } from 'node:crypto';
import { access, mkdir, mkdtemp, readFile, rm, writeFile } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { isAbsolute, join, relative, resolve } from 'node:path';
import test from 'node:test';
import { prepareRelease } from './prepare-release.mjs';

async function fixture(t, overrides = {}) {
  const tempRoot = resolve(tmpdir());
  const root = await mkdtemp(join(tempRoot, 'nar-htaung-site-test-'));
  t.after(async () => {
    const child = relative(tempRoot, resolve(root));
    assert.ok(child.startsWith('nar-htaung-site-test-') && !child.includes('..') && !isAbsolute(child));
    await rm(root, { recursive: true, force: true });
  });
  const config = {
    version: '1.2', channel: 'Beta', minimumAndroidVersion: '7.0',
    architecture: '64-bit', downloadUrl: '', fileName: 'nar-htaung-lite-1.2.apk',
    fileSizeBytes: null, sha256: '', ...overrides,
  };
  await mkdir(join(root, 'public'), { recursive: true });
  await writeFile(join(root, 'release.config.json'), JSON.stringify(config));
  return root;
}

test('local build copies the selected APK and records its actual size and checksum', async t => {
  const root = await fixture(t);
  const bytes = Buffer.from([0x50, 0x4b, 0x03, 0x04, 0x31, 0x32]);
  const source = join(root, 'signed.apk');
  await writeFile(source, bytes);
  const release = await prepareRelease({ root, env: { APK_SOURCE: source } });
  assert.equal(release.downloadUrl, '/downloads/nar-htaung-lite-1.2.apk');
  assert.equal(release.fileSizeBytes, bytes.length);
  assert.equal(release.sha256, createHash('sha256').update(bytes).digest('hex'));
  assert.deepEqual(await readFile(join(root, 'public', release.downloadUrl)), bytes);
  assert.deepEqual(JSON.parse(await readFile(join(root, 'public/release.json'), 'utf8')), release);
  assert.match(await readFile(join(root, 'src/app/release.generated.ts'), 'utf8'), /export const release/);
});

test('Vercel uses the configured public APK and removes stale local download output', async t => {
  const root = await fixture(t);
  await mkdir(join(root, 'public/downloads'));
  await writeFile(join(root, 'public/downloads/old.apk'), 'old local build');
  const url = 'https://github.com/example/music/releases/download/v1.2/music.apk';
  const release = await prepareRelease({ root, env: { VERCEL: '1', APK_DOWNLOAD_URL: url, APP_VERSION: '1.2.1' } });
  assert.equal(release.downloadUrl, url);
  assert.equal(release.version, '1.2.1');
  assert.equal(release.fileSizeBytes, null);
  await assert.rejects(access(join(root, 'public/downloads')), { code: 'ENOENT' });
});

test('Vercel fails clearly without a public APK URL instead of publishing a broken button', async t => {
  const root = await fixture(t);
  await mkdir(join(root, 'src/app'), { recursive: true });
  await writeFile(join(root, 'src/app/release.generated.ts'), 'stale release');
  await assert.rejects(prepareRelease({ root, env: { VERCEL: '1' } }), /Set APK_DOWNLOAD_URL in Vercel/);
  await assert.rejects(access(join(root, 'src/app/release.generated.ts')), { code: 'ENOENT' });
});

test('rejects insecure URLs, embedded credentials and links that do not point to an APK', async t => {
  const root = await fixture(t);
  for (const url of ['http://example.com/app.apk', 'https://user:password@example.com/app.apk', 'https://example.com/release']) {
    await assert.rejects(prepareRelease({ root, env: { APK_DOWNLOAD_URL: url } }), /absolute HTTPS APK URL/);
  }
});

test('rejects path traversal in the generated APK filename', async t => {
  const root = await fixture(t, { fileName: '../../outside.apk' });
  await assert.rejects(prepareRelease({ root, env: {} }), /simple .apk filename/);
});

test('rejects an invalid APK source before generating release data', async t => {
  const root = await fixture(t);
  const source = join(root, 'invalid.apk');
  await writeFile(source, 'this is not an APK');
  await assert.rejects(prepareRelease({ root, env: { APK_SOURCE: source } }), /not an APK/);
  await assert.rejects(access(join(root, 'src/app/release.generated.ts')), { code: 'ENOENT' });
});
