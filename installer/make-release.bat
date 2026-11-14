@echo off
setlocal
echo ============================================
echo  ScreenTime Guardian - release package build
echo  Produces: release\ScreenTimeGuardian-YYYYMMDD.zip
echo  Self-contained: targets need NO .NET runtime.
echo ============================================
cd /d "%~dp0.."

for /f "delims=" %%I in ('powershell -NoProfile -Command "Get-Date -Format yyyyMMdd"') do set STAMP=%%I
set OUT=release\ScreenTimeGuardian-%STAMP%

rmdir /s /q "%OUT%" 2>nul
mkdir "%OUT%\files"

echo [1/4] Publishing Service (self-contained win-x64)...
dotnet publish src\ScreenTimeGuardian.Service\ScreenTimeGuardian.Service.csproj -c Release -r win-x64 --self-contained true -o "%OUT%\files" --nologo -v q
if errorlevel 1 goto :fail

echo [2/4] Publishing LockScreen...
dotnet publish src\ScreenTimeGuardian.LockScreen\ScreenTimeGuardian.LockScreen.csproj -c Release -r win-x64 --self-contained true -o "%OUT%\files" --nologo -v q
if errorlevel 1 goto :fail

echo [3/4] Publishing Config...
dotnet publish src\ScreenTimeGuardian.Config\ScreenTimeGuardian.Config.csproj -c Release -r win-x64 --self-contained true -o "%OUT%\files" --nologo -v q
if errorlevel 1 goto :fail

if not exist "%OUT%\files\ScreenTimeGuardian.Service.exe" goto :fail
if not exist "%OUT%\files\ScreenTimeGuardian.LockScreen.exe" goto :fail
if not exist "%OUT%\files\ScreenTimeGuardian.Config.exe" goto :fail

copy /Y installer\install.bat "%OUT%\" >nul
copy /Y installer\INSTALL.txt "%OUT%\" >nul

echo [4/4] Zipping...
powershell -NoProfile -Command "Compress-Archive -Path '%OUT%\*' -DestinationPath '%OUT%.zip' -Force"
if errorlevel 1 goto :fail

echo.
echo DONE: %OUT%.zip
exit /b 0

:fail
echo.
echo RELEASE FAILED - see errors above.
exit /b 1
