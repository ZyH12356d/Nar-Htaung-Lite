# Nar Htaung Lite website

A static Angular landing page for the Android app, styled with the app's charcoal and mint branding. It includes app previews, features, a versioned APK download, installation instructions, FAQs, and privacy notes. It has no backend, accounts, or analytics scripts.

## Deploy the source on Vercel

The public APK link is already configured in `release.config.json`:

[Download Nar Htaung Lite 1.2](https://github.com/ZyH12356d/nar-htaung-releases/releases/download/v1.2/nar-htaung-lite-1.2.apk)

The download was verified publicly and matches the signed Android Release APK. Its size and SHA-256 checksum are recorded in the same configuration file.

1. Upload the **website source folder** to [Vercel Drop](https://vercel.com/drop), or drop the updated folder onto your existing project in Vercel.
2. The upload root should contain `package.json`, `vercel.json`, `release.config.json`, `src`, `scripts`, and `public`.
3. Use Angular as the framework. The included configuration sets Build Command to `npm run build` and Output Directory to `dist/nar-htaung-website/browser`. Use Node.js 24.x.
4. Leave Root Directory empty when uploading the `website` folder itself. If importing the entire Git repository instead, set Root Directory to `website`.
5. Deploy. Vercel builds the website automatically and its download buttons link to the APK on GitHub Releases.
6. Once the deployment is Ready, open the site on Android and test the APK download.

**No local website build or APK_DOWNLOAD_URL environment variable is required.** If you previously configured that variable in Vercel, remove it to use the URL in `release.config.json`, or ensure it contains the same valid APK URL. Environment variables override the file.

The earlier missing-URL error is resolved by the configured public release link. The `npm fund`, `allowScripts`, and `cache-dir` messages in the reported deployment log were warnings; the missing APK URL stopped that build.

## Publish a new app version

Upload the new signed APK as a GitHub Release asset, then update `release.config.json` with its version, direct APK URL, filename, byte size, and SHA-256 checksum. Keep the Android requirements consistent with that APK. Upload or push the updated website source to trigger a new Vercel deployment.

Use the same Android signing key for future app updates. A separate public releases repository lets the app source remain in its existing repository.

## Local development (optional)

Requires Node.js 24.15 or newer in the Node 24 release line and npm.

```powershell
cd D:\NHL\Nar-Htaung-Lite\website
npm.cmd ci
npm.cmd start
```

Open [http://localhost:4200](http://localhost:4200). These PowerShell examples use `npm.cmd` so they work when PowerShell script execution is disabled. The configured public APK is used during development too; a local Android build is unnecessary for website development.

## Release configuration

Edit `release.config.json` when the app version or Android requirements change. Build preparation generates `src/app/release.generated.ts` and `public/release.json`; do not edit those generated files.

- `downloadUrl`: the public HTTPS URL ending in `.apk`.
- `fileName`: preferred download filename.
- `fileSizeBytes` and `sha256`: metadata for the published APK. Update them together with the release URL.
- `version`, `channel`, `minimumAndroidVersion`, and `architecture`: must describe the published APK.

Optional environment overrides:

| Variable | Purpose |
| --- | --- |
| `APK_DOWNLOAD_URL` | Override the public APK URL in the configuration file. |
| `APP_VERSION` | Override the displayed app version. |
| `APK_SOURCE` | Select a local APK when no public URL is configured. |

`.env.example` documents these settings. Environment files are not loaded automatically by the preparation script; set overrides in your shell or in Vercel.

For local APK testing, clear `downloadUrl`, leave `APK_DOWNLOAD_URL` unset, and set `APK_SOURCE` to the signed APK path. Without an explicit source, local builds use the mobile project's existing Release APK. A Vercel source build requires the public URL, since the Android build output is outside the website upload.

Generated release files, local APK copies, dependencies, and build output are ignored by Git. A public-URL build removes generated local downloads and keeps the APK out of website deployments.

## Build and checks

```powershell
npm.cmd test
npm.cmd run build
```

The tests check release selection, cloud builds without environment overrides, APK integrity metadata, stale-output cleanup, and invalid configuration. Angular's production build checks TypeScript and templates and applies bundle budgets.

An optional `npm.cmd run build:drop` command prepares the compiled `dist/nar-htaung-website/browser` folder for static-only uploads. It is separate from the recommended source-upload workflow above.

## Assets

`public/images/app-*.png` previews were captured from the app's UI preview using a sample library. The site labels them as sample content. `app-logo.png` reuses the approved app artwork; `brand-mark.svg` follows the app's launcher symbol. Fonts use the device's available system fonts, so the site makes no external font requests.
