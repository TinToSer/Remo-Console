# Builds Remo-Console with the C# compiler that ships with Windows (.NET Framework 4.x). No SDK needed.
param(
    [string]$OutDir,
    [string]$Version,
    [switch]$Release
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "C# compiler not found at $csc" }

$out  = if ($OutDir) { $OutDir } else { Join-Path $root 'bin' }
$ico  = Join-Path $root 'assets\app.ico'
New-Item -ItemType Directory -Force $out, (Split-Path $ico) | Out-Null

# --- resolve semantic version (from -Version or VERSION file) ---
if (-not $Version) {
    $verFile = Join-Path $root 'VERSION'
    $Version = if (Test-Path $verFile) { (Get-Content $verFile -Raw).Trim() } else { '1.0.0' }
}
$cleanVer = $Version.TrimStart('v', 'V')
if ($cleanVer -notmatch '^\d+\.\d+\.\d+(?:\.\d+)?(?:-[0-9A-Za-z.-]+)?$') {
    throw "Invalid version format: '$Version' (expected semver like 1.0.0)"
}
$numericVer = ($cleanVer -split '-')[0]
$parts = $numericVer.Split('.')
while ($parts.Count -lt 4) { $parts += '0' }
$asmVer = ($parts[0..3] -join '.')

# --- generate the application icon once (PNG-in-ICO, 16..256px) ---
if (-not (Test-Path $ico)) {
    Add-Type -AssemblyName System.Drawing
    function New-Png([int]$s) {
        $bmp = New-Object Drawing.Bitmap $s, $s
        $g = [Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'; $g.Clear([Drawing.Color]::Transparent)
        $r = [int]($s * 0.22)
        $p = New-Object Drawing.Drawing2D.GraphicsPath
        $p.AddArc(0, 0, 2*$r, 2*$r, 180, 90); $p.AddArc($s-2*$r-1, 0, 2*$r, 2*$r, 270, 90)
        $p.AddArc($s-2*$r-1, $s-2*$r-1, 2*$r, 2*$r, 0, 90); $p.AddArc(0, $s-2*$r-1, 2*$r, 2*$r, 90, 90); $p.CloseFigure()
        $br = New-Object Drawing.Drawing2D.LinearGradientBrush ([Drawing.Point]::new(0,0)), ([Drawing.Point]::new(0,$s)), ([Drawing.Color]::FromArgb(255,104,158,255)), ([Drawing.Color]::FromArgb(255,58,112,230))
        $g.FillPath($br, $p)
        $pen = New-Object Drawing.Pen ([Drawing.Color]::White), ([single]($s * 0.07))
        $pen.LineJoin = 'Round'
        $mx = $s*0.2; $my = $s*0.24; $mw = $s*0.6; $mh = $s*0.4
        $g.DrawRectangle($pen, $mx, $my, $mw, $mh)
        $g.DrawLine($pen, $s*0.5, $my+$mh, $s*0.5, $my+$mh+$s*0.1)
        $g.DrawLine($pen, $s*0.34, $my+$mh+$s*0.1, $s*0.66, $my+$mh+$s*0.1)
        $ms = New-Object IO.MemoryStream; $bmp.Save($ms, [Drawing.Imaging.ImageFormat]::Png); $g.Dispose(); $bmp.Dispose()
        ,$ms.ToArray()
    }
    $sizes = 16, 32, 48, 256
    $pngs = $sizes | ForEach-Object { ,(New-Png $_) }
    $fs = [IO.File]::Create($ico); $w = New-Object IO.BinaryWriter $fs
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $s = $sizes[$i]; $d = if ($s -ge 256) { 0 } else { $s }
        $w.Write([byte]$d); $w.Write([byte]$d); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32); $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($p in $pngs) { $w.Write($p) }
    $w.Close(); $fs.Close()
}

# --- generate temporary version info file (keeps working tree clean) ---
$verCs = Join-Path ([IO.Path]::GetTempPath()) ("Remo-Console.Version." + [Guid]::NewGuid().ToString("N") + ".cs")
@"
using System.Reflection;
[assembly: AssemblyVersion("$asmVer")]
[assembly: AssemblyFileVersion("$asmVer")]
[assembly: AssemblyInformationalVersion("$cleanVer")]
"@ | Set-Content -Path $verCs -Encoding UTF8

try {
    $refs = 'System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Security.dll','System.Web.Extensions.dll','Microsoft.CSharp.dll'
    $debugFlag = if ($Release) { '/debug-' } else { '/debug:pdbonly' }
    $sources = (Get-ChildItem "$root\src\*.cs" | ForEach-Object FullName) + @($verCs)
    $args = @('/nologo','/target:winexe','/platform:x64','/optimize+',$debugFlag,'/warn:4','/warnaserror+',"/win32icon:$ico","/out:$out\Remo-Console.exe") +
            ($refs | ForEach-Object { "/r:$_" }) + $sources
    & $csc @args
    if ($LASTEXITCODE -ne 0) { throw "Build failed (exit code $LASTEXITCODE)" }
    if ($Release) {
        $pdb = Join-Path $out 'Remo-Console.pdb'
        if (Test-Path $pdb) { Remove-Item $pdb -Force }
    }
    Write-Host "Built $out\Remo-Console.exe (v$cleanVer$(if ($Release) { ', release' }))"
}
finally {
    if (Test-Path $verCs) { Remove-Item $verCs -Force -ErrorAction SilentlyContinue }
}
