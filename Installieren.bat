@echo off
chcp 65001 >nul
setlocal
title POCSAG Decoder installieren
rem Installiert den POCSAG Decoder fuer den angemeldeten Benutzer (keine Adminrechte noetig).
rem Programmdateien: Unterordner "Programm" neben diesem Skript, sonst der Ordner des Skripts selbst
rem (PocsagDecoder.exe, rtl_fm.exe, multimon-ng.exe und DLLs).
rem Optional: "Einstellungen\settings.json" neben dem Skript wird als Einstellungen uebernommen (Umzug auf einen anderen PC).
set "SRC=%~dp0"
if exist "%SRC%Programm\PocsagDecoder.exe" set "SRC=%SRC%Programm\"
set "DEST=%LOCALAPPDATA%\Programs\PocsagDecoder"
set "CFG=%APPDATA%\PocsagDecoder"

if not exist "%SRC%PocsagDecoder.exe" (
  echo PocsagDecoder.exe nicht gefunden. Das Skript muss im selben Ordner wie PocsagDecoder.exe liegen.
  pause
  exit /b 1
)

echo POCSAG Decoder wird installiert ...
echo.
tasklist /FI "IMAGENAME eq PocsagDecoder.exe" | find /I "PocsagDecoder.exe" >nul && (
  echo Der POCSAG Decoder laeuft gerade. Bitte zuerst beenden ^(Tray-Symbol, Rechtsklick, Beenden^).
  pause
  exit /b 1
)

mkdir "%DEST%" 2>nul
xcopy "%SRC%*.exe" "%DEST%\" /Y /Q >nul || (echo Kopieren fehlgeschlagen. & pause & exit /b 1)
xcopy "%SRC%*.dll" "%DEST%\" /Y /Q >nul 2>nul
if exist "%SRC%README.md" copy /Y "%SRC%README.md" "%DEST%\" >nul
echo [OK] Programm nach %DEST% kopiert
if not exist "%DEST%\rtl_fm.exe" echo [!] rtl_fm.exe fehlt - bitte nach %DEST% legen ^(siehe README^)
if not exist "%DEST%\multimon-ng.exe" echo [!] multimon-ng.exe fehlt - bitte nach %DEST% legen ^(siehe README^)

rem Die kleine Exe braucht die .NET 8 Desktop Runtime, die grosse (selbststaendige) nicht.
for %%F in ("%DEST%\PocsagDecoder.exe") do if %%~zF GTR 20000000 goto haveruntime
dotnet --list-runtimes 2>nul | find "Microsoft.WindowsDesktop.App 8." >nul && goto haveruntime
echo .NET 8 Desktop Runtime fehlt, wird ueber winget installiert ...
winget install --id Microsoft.DotNet.DesktopRuntime.8 -e --accept-source-agreements --accept-package-agreements
if errorlevel 1 (
  echo Automatische Installation nicht moeglich. Bitte die ".NET Desktop Runtime 8" fuer Windows x64 von Microsoft installieren.
  start "" "https://dotnet.microsoft.com/download/dotnet/8.0"
)
:haveruntime

if not exist "%~dp0Einstellungen\settings.json" goto skipcfg
mkdir "%CFG%" 2>nul
if exist "%CFG%\settings.json" (
  choice /C JN /M "Auf diesem PC gibt es schon Einstellungen. Durch die mitgebrachten ersetzen"
  if errorlevel 2 goto skipcfg
  copy /Y "%CFG%\settings.json" "%CFG%\settings.vorher.json" >nul
)
copy /Y "%~dp0Einstellungen\settings.json" "%CFG%\settings.json" >nul
echo [OK] Einstellungen, RIC-Beschreibungen, Telegrammereignisse und Schnittstellen uebernommen
:skipcfg

powershell -NoProfile -ExecutionPolicy Bypass -Command ^
 "$s=New-Object -ComObject WScript.Shell; foreach($p in @([Environment]::GetFolderPath('Desktop'), (Join-Path $env:APPDATA 'Microsoft\Windows\Start Menu\Programs'))){ $l=$s.CreateShortcut((Join-Path $p 'POCSAG Decoder.lnk')); $l.TargetPath='%DEST%\PocsagDecoder.exe'; $l.WorkingDirectory='%DEST%'; $l.Save() }"
echo [OK] Verknuepfung auf dem Desktop und im Startmenue angelegt
echo.
echo ------------------------------------------------------------
echo Noch EIN Schritt von Hand (nur einmal pro PC):
echo   Treiber fuer den RTL-SDR-Stick mit Zadig installieren.
echo   Stick einstecken, Zadig starten, Options - List All Devices,
echo   "Bulk-In, Interface (Interface 0)" waehlen, WinUSB, Replace Driver.
echo ------------------------------------------------------------
choice /C JN /M "Zadig-Downloadseite jetzt oeffnen"
if errorlevel 2 goto start
start "" "https://zadig.akeo.ie/"
:start
echo.
choice /C JN /M "POCSAG Decoder jetzt starten"
if errorlevel 2 exit /b 0
start "" "%DEST%\PocsagDecoder.exe"
