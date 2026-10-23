param([string]$OutputDirectory, [string]$WindowsMetadataDirectory)
$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) { $OutputDirectory = Join-Path $PSScriptRoot 'bin' }
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'Install .NET Framework 4.8 on 64-bit Windows before building.' }
$framework = Split-Path $compiler
$refs = @('System.dll','System.Core.dll','System.Net.Http.dll','System.Security.dll','System.ServiceProcess.dll','System.Web.Extensions.dll','System.Xaml.dll','System.Windows.Forms.dll','System.Drawing.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')
$argsList = @('/nologo','/optimize+','/target:winexe','/platform:anycpu','/langversion:5',('/out:' + (Join-Path $OutputDirectory 'EeroDesktop.exe')),('/win32manifest:' + (Join-Path $PSScriptRoot 'source\app.manifest')))
$argsList += '/win32icon:' + (Join-Path $PSScriptRoot 'assets\app-icon.ico')
$argsList += '/resource:' + (Join-Path $PSScriptRoot 'assets\app-icon.ico') + ',EeroDesktop.AppIcon.ico'
$argsList += '/resource:' + (Join-Path $PSScriptRoot 'assets\app-icon.png') + ',EeroDesktop.AppIcon.png'
foreach ($ref in $refs) { $argsList += '/reference:' + (Join-Path $framework $ref) }
$argsList += '/reference:' + (Join-Path $PSScriptRoot 'QRCoder.dll')
foreach ($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll')) { $argsList += '/reference:' + (Join-Path $PSScriptRoot $name) }
$runtimeFacade = Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.dll'
$argsList += '/reference:' + $runtimeFacade
$argsList += '/reference:' + (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Runtime.InteropServices.WindowsRuntime\v4.0_4.0.0.0__b03f5f7f11d50a3a\System.Runtime.InteropServices.WindowsRuntime.dll')
$argsList += '/reference:' + (Join-Path $framework 'System.Runtime.WindowsRuntime.dll')
if ([string]::IsNullOrWhiteSpace($WindowsMetadataDirectory)) {
    $cache = Join-Path $PSScriptRoot '.build\windows-contracts'
    $WindowsMetadataDirectory = Join-Path $cache 'sdk\ref\netstandard2.0'
    if (-not (Test-Path -LiteralPath (Join-Path $WindowsMetadataDirectory 'Windows.WinMD'))) {
        New-Item -ItemType Directory -Path $cache -Force | Out-Null
        Write-Output 'Restoring Microsoft Windows SDK compile-time contracts 10.0.19041.1 from NuGet.'
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.windows.sdk.contracts/10.0.19041.1/microsoft.windows.sdk.contracts.10.0.19041.1.nupkg' -OutFile (Join-Path $cache 'contracts.zip')
        Expand-Archive -LiteralPath (Join-Path $cache 'contracts.zip') -DestinationPath (Join-Path $cache 'sdk') -Force
    }
}
$argsList += '/lib:' + $WindowsMetadataDirectory
foreach ($name in @('Windows.WinMD','Windows.Foundation.FoundationContract.winmd','Windows.Foundation.UniversalApiContract.winmd')) { $argsList += '/reference:' + (Join-Path $WindowsMetadataDirectory $name) }
$argsList += @(Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'source') -Filter '*.cs' | ForEach-Object FullName)
foreach ($asset in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets\products') -Filter '*.png') { $argsList += '/resource:' + $asset.FullName + ',EeroDesktop.Products.' + $asset.Name }
& $compiler @argsList
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
if (([System.IO.Path]::GetFullPath($OutputDirectory)) -ne $PSScriptRoot) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QRCoder.dll') -Destination $OutputDirectory -Force
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'EeroDesktop.exe.config') -Destination $OutputDirectory -Force
    foreach ($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','THIRD-PARTY-NOTICES.txt','LICENSE-WebView2.txt','LICENSE-Guardian.txt')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $OutputDirectory -Force }
    foreach ($arch in @('win-x64','win-x86','win-arm64')) { $relative = 'runtimes\' + $arch + '\native'; $target = Join-Path $OutputDirectory $relative; New-Item -ItemType Directory -Path $target -Force | Out-Null; Copy-Item -LiteralPath (Join-Path $PSScriptRoot ($relative + '\WebView2Loader.dll')) -Destination $target -Force }
}
Write-Output (Join-Path $OutputDirectory 'EeroDesktop.exe')
