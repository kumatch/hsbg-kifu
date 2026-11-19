<#
.SYNOPSIS
  Hearthstone のログフォルダから Power.log と GameNetLogger.log を gzip で退避する（TASKS.md M0）。

.DESCRIPTION
  Hearthstone は起動ごとに Logs\Hearthstone_<日時>\ を作り、古いフォルダは 6 起動分ほどで消える。
  記録アプリができるまでの間、このスクリプトで Power.log を退避しておけば、後で記録アプリの作り直しコマンドに通せる。

  - 対象: <Hearthstone のフォルダ>\Logs\Hearthstone_*\ の Power*.log と GameNetLogger*.log
  - 退避先: <Destination>\Hearthstone_<日時>\<元の名前>.gz（元のファイルの更新日時を付ける）
  - 退避先に同じ更新日時のファイルがあれば飛ばす。Hearthstone の起動中の Power.log も読める（共有モードで開く）。
    終了後に Power_old.log になったら、それを退避して、古い Power.log.gz は消す（Power_old.log は Power.log の名前を変えたもの）
  - Hearthstone のファイルは読むだけで、変えない

  退避したファイルにはロビー全員の BattleTag が入る。人に渡さない。

.PARAMETER Destination
  退避先。既定は %USERPROFILE%\Documents\HSBG-PowerLogs

.PARAMETER HearthstoneDir
  Hearthstone のフォルダ。既定は HDT の config.xml の HearthstoneDirectory、無ければ C:\Program Files (x86)\Hearthstone

.EXAMPLE
  .\scripts\backup-powerlogs.ps1
  .\scripts\backup-powerlogs.ps1 -Destination D:\HSBG-PowerLogs
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'HSBG-PowerLogs'),
    [string]$HearthstoneDir
)

$ErrorActionPreference = 'Stop'

function Get-HearthstoneDir {
    $cfg = Join-Path $env:APPDATA 'HearthstoneDeckTracker\config.xml'
    if (Test-Path -LiteralPath $cfg) {
        try {
            [xml]$x = Get-Content -LiteralPath $cfg -Raw -Encoding UTF8
            $d = $x.Config.HearthstoneDirectory
            if ($d -and (Test-Path -LiteralPath $d)) { return $d }
        } catch {
            Write-Warning "HDT の config.xml を読めませんでした: $($_.Exception.Message)"
        }
    }
    return 'C:\Program Files (x86)\Hearthstone'
}

function Compress-LogFile([System.IO.FileInfo]$Source, [string]$Target) {
    $tmp = "$Target.tmp"
    $share = [System.IO.FileShare]::ReadWrite -bor [System.IO.FileShare]::Delete
    $in = [System.IO.File]::Open($Source.FullName, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, $share)
    try {
        $out = [System.IO.File]::Create($tmp)
        try {
            $gz = New-Object System.IO.Compression.GZipStream($out, [System.IO.Compression.CompressionLevel]::Optimal)
            try { $in.CopyTo($gz, 1MB) } finally { $gz.Dispose() }
        } finally { $out.Dispose() }
    } finally { $in.Dispose() }
    if (Test-Path -LiteralPath $Target) { Remove-Item -LiteralPath $Target -Force }
    Move-Item -LiteralPath $tmp -Destination $Target
    (Get-Item -LiteralPath $Target).LastWriteTimeUtc = $Source.LastWriteTimeUtc
}

if (-not $HearthstoneDir) { $HearthstoneDir = Get-HearthstoneDir }
$logsDir = Join-Path $HearthstoneDir 'Logs'
if (-not (Test-Path -LiteralPath $logsDir)) {
    Write-Error "Hearthstone のログフォルダがありません: $logsDir（-HearthstoneDir で指定できます）"
    exit 1
}
New-Item -ItemType Directory -Force -Path $Destination | Out-Null

$copied = 0; $skipped = 0; $failed = 0; $bytes = 0L
$sessions = Get-ChildItem -LiteralPath $logsDir -Directory -Filter 'Hearthstone_*' | Sort-Object Name
foreach ($s in $sessions) {
    $files = @(Get-ChildItem -LiteralPath $s.FullName -File |
        Where-Object { $_.Name -like 'Power*.log' -or $_.Name -like 'GameNetLogger*.log' })
    if ($files.Count -eq 0) { continue }
    $destDir = Join-Path $Destination $s.Name
    New-Item -ItemType Directory -Force -Path $destDir | Out-Null
    foreach ($f in $files) {
        $target = Join-Path $destDir ($f.Name + '.gz')
        if ((Test-Path -LiteralPath $target) -and (Get-Item -LiteralPath $target).LastWriteTimeUtc -eq $f.LastWriteTimeUtc) {
            $skipped++
            continue
        }
        try {
            Compress-LogFile $f $target
            $copied++; $bytes += $f.Length
            Write-Host ("退避: {0}\{1}（{2:N0} MB）" -f $s.Name, $f.Name, ($f.Length / 1MB))
        } catch {
            $failed++
            Write-Warning "退避できませんでした: $($f.FullName): $($_.Exception.Message)"
        }
    }
    # 終了後に名前が変わった（Power.log が無く _old.log がある）なら、起動中に退避した古い写しを消す
    foreach ($name in @('Power', 'GameNetLogger')) {
        $live = Join-Path $s.FullName "$name.log"
        $old = Join-Path $destDir "${name}_old.log.gz"
        $stale = Join-Path $destDir "$name.log.gz"
        if (-not (Test-Path -LiteralPath $live) -and (Test-Path -LiteralPath $old) -and (Test-Path -LiteralPath $stale)) {
            Remove-Item -LiteralPath $stale -Force
            Write-Host "古い写しを消しました: $($s.Name)\$name.log.gz（${name}_old.log.gz に置き換わった）"
        }
    }
}

Write-Host ("完了: 退避 {0} 件（元 {1:N0} MB）、変化なし {2} 件、失敗 {3} 件。退避先: {4}" -f $copied, ($bytes / 1MB), $skipped, $failed, $Destination)
if ($failed -gt 0) { exit 1 }
