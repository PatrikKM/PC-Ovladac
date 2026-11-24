$ErrorActionPreference = "Stop"

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    throw "Kompilátor csc.exe nebyl nalezen na $csc"
}

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$outExe = Join-Path $root "PC-Ovladac.exe"
$sources = Get-ChildItem (Join-Path $root "src") -Filter "*.cs" | ForEach-Object { $_.FullName }

& $csc /nologo /target:winexe /optimize+ /out:$outExe `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    @sources

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Kompilace úspěšná: $outExe"
