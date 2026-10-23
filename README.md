# eero Desktop (Unofficial) — v1.0

An independent Windows application for managing an eero network, with a native desktop layout, light and dark themes, device artwork, and Amazon or email/phone sign-in.

Made by: **Carlos B. (Los33)**

This project is not affiliated with or endorsed by Amazon or eero. Feature availability depends on your hardware, account permissions, and service entitlements. See [FEATURE-COVERAGE.txt](FEATURE-COVERAGE.txt) for the feature catalog and current limitations.

## Requirements

- Windows 10 or 11, 64-bit, with .NET Framework 4.8.
- Windows PowerShell 5.1 or PowerShell 7 to run the build script.
- Internet access for the first build to restore the pinned Windows SDK contracts from NuGet.
- Microsoft Edge WebView2 Runtime to use the app's embedded sign-in and service pages.

The build uses the C# compiler supplied with .NET Framework. Visual Studio, the modern .NET SDK, an Amazon developer account, and an eero Business subscription are not required to build this project or use consumer Amazon sign-in.

## Build and run

Open PowerShell in the repository folder and run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Build.ps1
.\bin\EeroDesktop.exe
```

The script compiles all C# files in `source`, embeds the application icons and product images, and copies runtime dependencies and third-party notices into `bin`. Keep that folder's contents together when distributing or running the app.

The first build downloads `Microsoft.Windows.SDK.Contracts` version `10.0.19041.1` into `.build`. Subsequent builds reuse that cache. These contracts are build metadata and are not required in the final runtime folder. To build offline, supply an already extracted package's `ref\netstandard2.0` directory:

```powershell
.\Build.ps1 -WindowsMetadataDirectory 'C:\path\to\contracts\ref\netstandard2.0'
```

Use `-OutputDirectory 'C:\path\to\output'` to choose a different output folder.

## Verify the build

Run the included self-tests without signing in or changing your network:

```powershell
$testLog = Join-Path (Resolve-Path .\bin).Path 'test-results.txt'
$test = Start-Process -FilePath .\bin\EeroDesktop.exe `
    -ArgumentList ('--self-test "{0}"' -f $testLog) `
    -WindowStyle Hidden -Wait -PassThru
Get-Content -LiteralPath $testLog
if ($test.ExitCode -ne 0) { throw 'Self-tests failed.' }
```

Open the interface using demonstration data:

```powershell
.\bin\EeroDesktop.exe --preview
```

Self-tests use simulated service responses. They do not establish that every account-specific service or hardware workflow works with a live account.

## Sign-in and local data

Consumer Amazon sign-in uses Amazon's sign-in page and requires the Amazon account already linked to your eero login. This app does not create, convert, or link accounts. Email/phone verification remains available for accounts configured to use it.

Remembered eero sessions are protected with Windows DPAPI for the current user. Local application data is stored under `%LOCALAPPDATA%\EeroDesktop`, outside the source folder. Do not commit account sessions, browser profiles, verification codes, or VPN configurations.

## Repository contents

| Path | Purpose |
| --- | --- |
| `source/` | Application code, built-in tests, and Windows manifest |
| `assets/` | Application icon, product images, and artwork source records |
| `runtimes/` | WebView2 native loaders for Windows x64, x86, and ARM64 |
| `QRCoder.dll` | Bundled QR-code library |
| `Microsoft.Web.WebView2.*.dll` | Bundled WebView2 managed libraries |
| `EeroDesktop.exe.config` | .NET Framework runtime configuration |
| `Build.ps1` | Build and runtime packaging script |
| `THIRD-PARTY-NOTICES.txt`, `LICENSE-*.txt` | Dependency notices and licenses |

The bundled libraries and native loaders are required; include them when uploading this repository. Generated binaries and the build cache are excluded by `.gitignore`.

eero product imagery and marks belong to their respective rights holders. Image source pages and hashes are recorded in [assets/products/sources.json](assets/products/sources.json); third-party license terms are listed in [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt).
