import { createHash } from 'node:crypto';
import { readFile, writeFile } from 'node:fs/promises';
import { dirname, isAbsolute, join, relative, resolve } from 'node:path';
import { fileURLToPath } from 'node:url';

const root = resolve(dirname(fileURLToPath(import.meta.url)), '..');
const output = resolve(root, 'dist/nar-htaung-website/browser');

try {
  await readFile(join(output, 'index.html'));
  const release = JSON.parse(await readFile(join(output, 'release.json'), 'utf8'));
  if (release.downloadUrl.startsWith('/')) {
    const apk = resolve(output, '.' + release.downloadUrl);
    const child = relative(output, apk);
    if (!child || child.startsWith('..') || isAbsolute(child))
      throw new Error('The APK must stay inside the static output directory.');
    const bytes = await readFile(apk);
    const hash = createHash('sha256').update(bytes).digest('hex');
    if (bytes.length !== release.fileSizeBytes || hash !== release.sha256)
      throw new Error('The bundled APK does not match its release metadata. Rebuild the website.');
  }

  const sourceConfig = JSON.parse(await readFile(join(root, 'vercel.json'), 'utf8'));
  // This config belongs to the compiled site only. Source deployments still use Angular.
  const staticConfig = {
    $schema: sourceConfig.$schema,
    framework: null,
    installCommand: '',
    buildCommand: '',
    outputDirectory: '.',
    headers: [
      ...sourceConfig.headers,
      {
        source: '/downloads/(.*)',
        headers: [
          { key: 'Content-Type', value: 'application/vnd.android.package-archive' },
          { key: 'Content-Disposition', value: 'attachment' },
        ],
      },
    ],
  };
  await writeFile(join(output, 'vercel.json'), JSON.stringify(staticConfig, null, 2) + '\n');
  console.log(`Ready for Vercel Drop: ${output}`);
  console.log(release.downloadUrl.startsWith('/')
    ? `Includes the signed Android APK (${(release.fileSizeBytes / 1_000_000).toFixed(1)} MB). No APK_DOWNLOAD_URL is needed for this folder.`
    : 'Uses your configured public APK download URL.');
} catch (error) {
  console.error(`Could not prepare the drop folder: ${error.message}`);
  process.exitCode = 1;
}
