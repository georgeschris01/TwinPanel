@echo off
rem Fabrique l'installateur de TwinPanel : installer-output\TwinPanel-Setup-<version>.exe
rem Il faut Inno Setup (gratuit) : https://jrsoftware.org/isdl.php
rem La version se change dans Directory.Build.props (balise Version) avant chaque nouvel installateur.
rem Resultat detaille dans installer.log.
cd /d "%~dp0"
setlocal

for /f "usebackq delims=" %%v in (`powershell -NoProfile -Command "(Select-Xml -Path 'Directory.Build.props' -XPath '//Version[not(@Condition)]').Node.InnerText"`) do set VERSION=%%v
if "%VERSION%"=="" set VERSION=0.0.0

set ISCC=
for %%p in ("%ProgramFiles%\Inno Setup 7\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 7\ISCC.exe" "%ProgramFiles%\Inno Setup 6\ISCC.exe" "%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe" "%LocalAppData%\Programs\Inno Setup 7\ISCC.exe" "%LocalAppData%\Programs\Inno Setup 6\ISCC.exe") do (
  if exist %%p set ISCC=%%p
)
if not defined ISCC for /f "delims=" %%i in ('where ISCC.exe 2^>nul') do set ISCC="%%i"

call :work > installer.log 2>&1

if exist "installer-output\TwinPanel-Setup-%VERSION%.exe" explorer /select,"installer-output\TwinPanel-Setup-%VERSION%.exe"
endlocal
goto :eof

:work
echo === Installateur TwinPanel %VERSION% ===
echo.
echo === Publication (Release, .NET inclus) ===
if exist publish\win-x64 rmdir /s /q publish\win-x64
dotnet publish src\TwinPanel.App\TwinPanel.App.csproj -c Release -r win-x64 --self-contained true -o publish\win-x64 -p:DebugType=none "-p:PathMap=%CD%=."
if errorlevel 1 (
  echo ECHEC de la publication.
  goto :done
)
rem Aucune donnee personnelle dans l'installateur : on retire tout ce qui ne fait pas partie du programme.
for %%f in (crash.log settings.json connections.json secrets.dat known_hosts.json history.json transfers.json transfers.json.lock sync-profiles.json) do if exist "publish\win-x64\%%f" del /q "publish\win-x64\%%f"
del /q /s "publish\win-x64\*.twinpanel-part" >nul 2>&1
echo Contenu publie (verification : aucun reglage, connexion ni journal) :
dir /b /s publish\win-x64 | findstr /i /r "\.log$ \\settings.*\.json$ \\connections.*\.json$ \\secrets\.dat$ \\known_hosts.*\.json$ \\history\.json$ \\transfers\.json \\sync-profiles\.json" && echo ATTENTION : fichier personnel trouve ci-dessus. || echo OK, rien de personnel.
echo.
echo === Inno Setup ===
if not defined ISCC (
  echo Inno Setup est introuvable. Installe-le depuis https://jrsoftware.org/isdl.php puis relance ce fichier.
  goto :done
)
echo Compilateur : %ISCC%
%ISCC% /DAppVersion=%VERSION% installer\TwinPanel.iss
if errorlevel 1 (
  echo ECHEC de l'installateur.
  goto :done
)
echo.
echo Installateur cree : installer-output\TwinPanel-Setup-%VERSION%.exe
:done
echo.
echo === FIN ===
goto :eof
