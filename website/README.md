# Nar Htaung Lite website

A static Angular landing page for the Android app, styled with the app's charcoal and mint branding. It includes app previews, features, a versioned APK download, installation instructions, FAQs, and privacy notes. It has no backend, accounts, or analytics scripts.

## Run locally

Requires Node.js 24.15 or newer in the Node 24 release line and npm.

```powershell
cd D:\NHL\Nar-Htaung-Lite\website
npm.cmd ci
npm.cmd start
```

Open [http://localhost:4200](http://localhost:4200). The start script prepares release metadata and copies the existing signed Release APK from the mobile project's build output into an ignored local downloads folder.

These PowerShell examples use `npm.cmd` so they work when PowerShell script execution is disabled.

If that APK has not been built yet, build the Android Release first, or select another APK:

```powershell
$env:APK_SOURCE = 'D:\path\to\your-signed-app.apk'
npm.cmd start
```

The website does not create or change signing keys. Use the same signing key for future Android updates.

## Release configuration

Edit `release.config.json` when the app version or Android requirements change. It is the source for visible release information. Build preparation generates `src/app/release.generated.ts` and `public/release.json`; do not edit those generated files.

- `downloadUrl`: leave empty for the local APK, or set a public HTTPS URL ending in `.apk`.
- `fileName`: download filename shown to browsers.
- `fileSizeBytes` and `sha256`: optional metadata for a remote release. Local APK metadata is calculated automatically.
- `version`, `channel`, `minimumAndroidVersion`, and `architecture`: must describe the APK you publish.

Environment variables override configuration:

| Variable | Purpose |
| --- | --- |
| `APK_DOWNLOAD_URL` | Public direct link to a signed APK; required on Vercel unless configured in `release.config.json`. |
| `APP_VERSION` | Override the displayed app version. |
| `APK_SOURCE` | Select a local APK when no public URL is configured. |

`.env.example` documents these settings. Environment files are not loaded automatically by the preparation script; set them in your shell or in Vercel.

The local APK and generated release files are ignored by Git. A build using a public URL removes generated local downloads so the APK is served by the release host.

## Publish on Vercel

1. Upload your signed APK to a **public GitHub Release** and copy the APK asset's direct HTTPS URL. A separate public releases repository works if the app's source repository is private. Keep the APK version and website metadata in sync.
2. Push the website source to GitHub and import that repository into Vercel.
3. Set the Vercel project's **Root Directory** to `website`.
4. Select Angular as the framework. The included `vercel.json` sets `npm run build` and output directory `dist/nar-htaung-website/browser`. Use Node.js 24.x.
5. Add `APK_DOWNLOAD_URL` in Vercel's environment settings, with the public APK link. Set it for Production and Preview if both should build.
6. Deploy, then open the production site on an Android phone and verify the download and installation.

Vercel builds deliberately fail when no public APK URL is configured. This prevents publishing a download button pointing at a missing file. The website has no server functions or database requirement. Check the host's current plan terms before publishing.

## Build and checks

```powershell
npm.cmd test
npm.cmd run build
```

`npm test` checks release selection, APK integrity metadata, stale-output cleanup, and invalid configuration. Angular's production build checks TypeScript and templates and applies bundle budgets.

For a production-style build with a remote APK:

```powershell
$env:APK_DOWNLOAD_URL = 'https://github.com/YOUR-ACCOUNT/YOUR-REPOSITORY/releases/download/v1.2/nar-htaung-lite-1.2.apk'
npm.cmd run build
```

Replace the example with your actual published asset URL. No public release URL was verified during the initial website setup.

## Assets

`public/images/app-*.png` previews were captured from the app's UI preview using a sample library. The site labels them as sample content. `app-logo.png` reuses the approved app artwork; `brand-mark.svg` follows the app's launcher symbol. Fonts use the device's available system fonts, so the site makes no external font requests.

Before publishing, replace the sample screenshots if desired, confirm the release metadata, and review the privacy copy against any future app changes.
