@echo off
rem Compile la solution et lance les tests. Le resultat va dans build.log.
cd /d "%~dp0"
(
  echo === SDK installes ===
  dotnet --list-sdks
  echo.
  echo === Build ===
  dotnet build TwinPanel.sln -c Debug
  echo.
  echo === Tests ===
  dotnet test tests\TwinPanel.Core.Tests\TwinPanel.Core.Tests.csproj --no-build
  echo.
  echo === FIN ===
) > build.log 2>&1
