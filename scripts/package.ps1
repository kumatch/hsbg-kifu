<#
.SYNOPSIS
  hsbg-kifu の配布用 zip を作る（TASKS.md M7）。

.DESCRIPTION
  常駐アプリ（src\HsbgKifu.App）を、.NET ランタイムを同梱した 1 つの exe（self-contained、win-x64）にして、
  説明・ライセンスと一緒に zip にする。利用者は展開して HsbgKifu.exe を起動するだけでよい（.NET のインストールは要らない）。
  .NET ランタイムは MIT ライセンスなので同梱して配布してよい。条件の著作権表示とライセンス文は licenses\ に入れる。

  版は Directory.Build.props の <Version>。exe にはビルドしたコミットも入り、設定画面と動作記録に出る（KifuVersion.Display）。
  そのため、作業ツリーにコミットしていない変更があるときと、タグ v<version> が HEAD 以外を指しているとき（版の上げ忘れ）は止める。

  出力: dist\hsbg-kifu-<version>-win-x64.zip と、その SHA-256 を書いた .sha256
  zip の中身:
    hsbg-kifu\HsbgKifu.exe
    hsbg-kifu\README.txt                 （scripts\dist-readme.txt。使い方と、初回起動時の Windows の警告）
    hsbg-kifu\LICENSE.txt                （このリポジトリの LICENSE）
    hsbg-kifu\THIRD_PARTY_NOTICES.md     （HearthDb、Newtonsoft.Json、.NET）
    hsbg-kifu\licenses\*.txt             （.NET ランタイムと Newtonsoft.Json のライセンス文。NuGet のパッケージから写す）

.EXAMPLE
  .\scripts\package.ps1
  .\scripts\package.ps1 -SkipTests
  .\scripts\package.ps1 -AllowDirty   # 試しに作るとき（コミットしていない変更があっても止めない。配布には使わない）
#>
[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$app = Join-Path $repoRoot 'src\HsbgKifu.App\HsbgKifu.App.csproj'
$distDir = Join-Path $repoRoot 'dist'
$staging = Join-Path $distDir 'staging'
$rid = 'win-x64'

function Get-MsBuildProperty([string]$name) {
    $json = & dotnet msbuild $app "-getProperty:$name" "-p:RuntimeIdentifier=$rid" '-p:Configuration=Release'
    if ($LASTEXITCODE -ne 0) { throw "dotnet msbuild -getProperty:$name に失敗しました" }
    return ($json | Out-String).Trim()
}

function Invoke-Git {
    $out = & git -C $repoRoot @args
    if ($LASTEXITCODE -ne 0) { throw "git $($args -join ' ') に失敗しました (exit $LASTEXITCODE)" }
    return ($out | Out-String).Trim()
}

# exe に入るコミットのハッシュが中身と合うように、コミットしていない変更があれば止める
$dirty = Invoke-Git status --porcelain
if ($dirty) {
    if (-not $AllowDirty) { throw "コミットしていない変更があります（git status）。コミットしてから作ってください（試すだけなら -AllowDirty）" }
    Write-Warning 'コミットしていない変更があります。この zip は配布に使わないでください'
}
$head = Invoke-Git rev-parse HEAD

if (-not $SkipTests) {
    Write-Host 'テストを実行します ...'
    & dotnet test (Join-Path $repoRoot 'HsbgKifu.sln') -c Release --nologo
    if ($LASTEXITCODE -ne 0) { throw "テストに失敗しました (exit $LASTEXITCODE)" }
}

$version = Get-MsBuildProperty 'Version'
$runtimeVersion = Get-MsBuildProperty 'BundledNETCoreAppPackageVersion'
$tfm = Get-MsBuildProperty 'TargetFramework'
Write-Host "hsbg-kifu $version（.NET ランタイム $runtimeVersion を同梱）"

# タグ v<version> がもうあるなら HEAD を指していること（前のリリースの後で <Version> を上げ忘れていないか）
$tagCommit = & git -C $repoRoot rev-parse -q --verify "refs/tags/v$version^{commit}"
if ($LASTEXITCODE -eq 0 -and $tagCommit -and $tagCommit.Trim() -ne $head) {
    throw "タグ v$version は別のコミット（$($tagCommit.Trim().Substring(0, 7))）を指しています。Directory.Build.props の <Version> を上げてください"
}

if (Test-Path $staging) { Remove-Item -Recurse -Force $staging }
$appDir = Join-Path $staging 'hsbg-kifu'
New-Item -ItemType Directory -Force -Path (Join-Path $appDir 'licenses') | Out-Null

Write-Host '1 つの exe にまとめます ...'
& dotnet publish $app -c Release -r $rid --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -o $appDir --nologo
if ($LASTEXITCODE -ne 0) { throw "dotnet publish に失敗しました (exit $LASTEXITCODE)" }
$exe = Join-Path $appDir 'HsbgKifu.exe'
if (-not (Test-Path $exe)) { throw "exe が見つかりません: $exe" }
# exe の版（AssemblyInformationalVersion）が「<Version>+HEAD のコミット」になっているか。設定画面と動作記録に出る値
$productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($exe).ProductVersion
if ($productVersion -ne "$version+$head") {
    throw "exe の版が $productVersion です（期待: $version+$head）"
}
$extra = Get-ChildItem $appDir -File | Where-Object { $_.Name -ne 'HsbgKifu.exe' }
if ($extra) { throw "exe 以外のファイルが出ました（1 つの exe になっていません）: $($extra.Name -join ', ')" }

# 自分のアセンブリに手元の絶対パス（ユーザー名を含む）が残っていないか（Directory.Build.props の PathMap が効いているか）。
# exe の中は圧縮されていて読めないので、exe にまとめる前の DLL（publish のビルド出力）を見る
$needles = @($repoRoot, $env:USERPROFILE) | Where-Object { $_ }
$buildDir = Join-Path $repoRoot "src\HsbgKifu.App\bin\Release\$tfm\$rid"
foreach ($name in @('HsbgKifu.dll', 'HsbgKifu.Core.dll')) {
    $dll = Join-Path $buildDir $name
    if (-not (Test-Path $dll)) { throw "確かめる DLL が見つかりません: $dll" }
    $bytes = [System.IO.File]::ReadAllBytes($dll)
    $texts = @(
        [System.Text.Encoding]::ASCII.GetString($bytes),
        [System.Text.Encoding]::Unicode.GetString($bytes, 0, $bytes.Length),
        [System.Text.Encoding]::Unicode.GetString($bytes, 1, $bytes.Length - 1)
    )
    foreach ($needle in $needles) {
        foreach ($text in $texts) {
            if ($text.IndexOf($needle, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) {
                throw "$name に手元のパスが入っています（PathMap を確認し、ビルドし直してください）"
            }
        }
    }
}

# 説明とライセンス
Copy-Item (Join-Path $PSScriptRoot 'dist-readme.txt') (Join-Path $appDir 'README.txt')
Copy-Item (Join-Path $repoRoot 'LICENSE') (Join-Path $appDir 'LICENSE.txt')
Copy-Item (Join-Path $repoRoot 'THIRD_PARTY_NOTICES.md') (Join-Path $appDir 'THIRD_PARTY_NOTICES.md')
$packages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$licenseFiles = [ordered]@{
    'dotnet-runtime-LICENSE.txt'             = "microsoft.netcore.app.runtime.$rid\$runtimeVersion\LICENSE.TXT"
    'dotnet-runtime-THIRD-PARTY-NOTICES.txt' = "microsoft.netcore.app.runtime.$rid\$runtimeVersion\THIRD-PARTY-NOTICES.TXT"
    'dotnet-windowsdesktop-LICENSE.txt'      = "microsoft.windowsdesktop.app.runtime.$rid\$runtimeVersion\LICENSE"
    'Newtonsoft.Json-LICENSE.txt'            = 'newtonsoft.json\13.0.3\LICENSE.md'
}
foreach ($name in $licenseFiles.Keys) {
    $src = Join-Path $packages $licenseFiles[$name]
    if (-not (Test-Path $src)) { throw "ライセンス文が見つかりません: $($licenseFiles[$name])（NuGet のキャッシュ）" }
    Copy-Item $src (Join-Path $appDir "licenses\$name")
}

# zip（区切りを '/' にするため ZipArchive で直接組む。Windows PowerShell 5.1 の Compress-Archive は '\' になる）
$zip = Join-Path $distDir "hsbg-kifu-$version-$rid.zip"
if (Test-Path $zip) { Remove-Item $zip -Force }
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::Open($zip, [System.IO.Compression.ZipArchiveMode]::Create)
try {
    Get-ChildItem $appDir -Recurse -File | Sort-Object FullName | ForEach-Object {
        $entry = 'hsbg-kifu/' + $_.FullName.Substring($appDir.Length + 1).Replace('\', '/')
        [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
            $archive, $_.FullName, $entry, [System.IO.Compression.CompressionLevel]::Optimal)
    }
}
finally {
    $archive.Dispose()
}
Remove-Item -Recurse -Force $staging

$hash = (Get-FileHash $zip -Algorithm SHA256).Hash.ToLowerInvariant()
$zipName = Split-Path -Leaf $zip
[System.IO.File]::WriteAllText("$zip.sha256", "$hash  $zipName`n", [System.Text.UTF8Encoding]::new($false))

Write-Host ''
Write-Host "作成しました: $zip" -ForegroundColor Green
Write-Host '中身:'
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $archive.Entries | ForEach-Object { Write-Host ("  {0}  {1:N0} bytes" -f $_.FullName, $_.Length) }
}
finally {
    $archive.Dispose()
}
Write-Host ("zip: {0:N1} MB" -f ((Get-Item $zip).Length / 1MB))
Write-Host "版: $productVersion"
Write-Host "SHA-256: $hash（リリースノートに書く）"
