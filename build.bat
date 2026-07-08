@echo off
chcp 65001 >nul
setlocal
cd /d "%~dp0"
rem 使用 Windows 自带的 .NET Framework 编译器，无需安装任何东西
set "CSC=C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
set "ICON_ARG="
set "TEMP_ICON=Countdown.icon.%RANDOM%%RANDOM%.tmp.ico"

if not exist "icon.png" goto compile
echo 检测到 icon.png，正在生成可执行文件图标...
powershell -NoProfile -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop';" ^
  "$png='icon.png'; $ico='%TEMP_ICON%';" ^
  "Add-Type -AssemblyName System.Drawing;" ^
  "$src=[System.Drawing.Image]::FromFile((Resolve-Path $png));" ^
  "try {" ^
  "  $size=[Math]::Min(256,[Math]::Max($src.Width,$src.Height));" ^
  "  if ($size -lt 1) { throw 'invalid image size' }" ^
  "  $bmp=New-Object System.Drawing.Bitmap -ArgumentList $size,$size,([System.Drawing.Imaging.PixelFormat]::Format32bppArgb);" ^
  "  try {" ^
  "    $g=[System.Drawing.Graphics]::FromImage($bmp);" ^
  "    try {" ^
  "      $g.Clear([System.Drawing.Color]::Transparent);" ^
  "      $g.InterpolationMode=[System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;" ^
  "      $g.PixelOffsetMode=[System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality;" ^
  "      $scale=[Math]::Min($size/[double]$src.Width,$size/[double]$src.Height);" ^
  "      $w=[int][Math]::Round($src.Width*$scale);" ^
  "      $h=[int][Math]::Round($src.Height*$scale);" ^
  "      $x=[int][Math]::Floor(($size-$w)/2);" ^
  "      $y=[int][Math]::Floor(($size-$h)/2);" ^
  "      $g.DrawImage($src,$x,$y,$w,$h);" ^
  "    } finally { $g.Dispose(); }" ^
  "    $ms=New-Object System.IO.MemoryStream;" ^
  "    try { $bmp.Save($ms,[System.Drawing.Imaging.ImageFormat]::Png); $bytes=$ms.ToArray(); } finally { $ms.Dispose(); }" ^
  "  } finally { $bmp.Dispose(); }" ^
  "} finally { $src.Dispose(); }" ^
  "$dim=0; if ($size -lt 256) { $dim=$size; }" ^
  "$bw=New-Object System.IO.BinaryWriter -ArgumentList ([System.IO.File]::Create($ico));" ^
  "try {" ^
  "  $bw.Write([UInt16]0); $bw.Write([UInt16]1); $bw.Write([UInt16]1);" ^
  "  $bw.Write([Byte]$dim); $bw.Write([Byte]$dim); $bw.Write([Byte]0); $bw.Write([Byte]0);" ^
  "  $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]$bytes.Length); $bw.Write([UInt32]22);" ^
  "  $bw.Write($bytes);" ^
  "} finally { $bw.Close(); }"
if errorlevel 1 goto icon_failed
set "ICON_ARG=/win32icon:%TEMP_ICON%"

:compile
"%CSC%" /nologo /codepage:65001 /target:winexe /optimize+ /win32manifest:Countdown.manifest %ICON_ARG% /out:Countdown.exe Countdown.cs
set "BUILD_ERROR=%ERRORLEVEL%"
if exist "%TEMP_ICON%" del /f /q "%TEMP_ICON%" >nul 2>nul
if "%BUILD_ERROR%"=="0" (echo 编译成功：Countdown.exe) else (echo 编译失败)
pause
exit /b %BUILD_ERROR%

:icon_failed
if exist "%TEMP_ICON%" del /f /q "%TEMP_ICON%" >nul 2>nul
echo 图标转换失败
pause
exit /b 1
