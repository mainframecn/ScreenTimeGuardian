@echo off
echo ============================================
echo  ScreenTime Guardian - Installation Script
echo  Run as Administrator!
echo ============================================
echo.

:: Navigate to project root (parent of installer folder)
cd /d "%~dp0.."

:: Check for admin rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: This script must be run as Administrator!
    echo Right-click and select "Run as administrator"
    pause
    exit /b 1
)

echo [1/6] Building solution...
where dotnet >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: .NET SDK not found!
    echo Please install .NET 8.0 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
    pause
    exit /b 1
)

dotnet publish src\ScreenTimeGuardian.Service\ScreenTimeGuardian.Service.csproj -c Release -o publish\service
if %errorlevel% neq 0 (
    echo ERROR: Build failed!
    pause
    exit /b 1
)

dotnet publish src\ScreenTimeGuardian.LockScreen\ScreenTimeGuardian.LockScreen.csproj -c Release -o publish\lockscreen
if %errorlevel% neq 0 (
    echo ERROR: LockScreen build failed - the machine would be left with no lock window.
    pause
    exit /b 1
)

dotnet publish src\ScreenTimeGuardian.Config\ScreenTimeGuardian.Config.csproj -c Release -o publish\config
if %errorlevel% neq 0 (
    echo ERROR: Config app build failed.
    pause
    exit /b 1
)

echo [2/6] Replacing installation directory...
:: Stop the watchdog BEFORE the service: it re-starts the service every minute,
:: and a publish that collides with a mid-copy restart leaves files locked (MSB3021)
:: or the install half-replaced. Watchdog is re-created at step [4/6].
schtasks /delete /tn "ScreenTimeGuardianWatchdog" /f >nul 2>&1
if exist "%ProgramFiles%\ScreenTimeGuardian" (
    net stop ScreenTimeGuardian >nul 2>&1
    rmdir /s /q "%ProgramFiles%\ScreenTimeGuardian"
)
mkdir "%ProgramFiles%\ScreenTimeGuardian" 2>nul
copy /Y publish\service\* "%ProgramFiles%\ScreenTimeGuardian\" >nul
copy /Y publish\lockscreen\* "%ProgramFiles%\ScreenTimeGuardian\" >nul
copy /Y publish\config\* "%ProgramFiles%\ScreenTimeGuardian\" >nul

if not exist "%ProgramFiles%\ScreenTimeGuardian\ScreenTimeGuardian.Service.exe" (
    echo ERROR: service executable missing after copy. Nothing further is installed.
    pause
    exit /b 1
)
if not exist "%ProgramFiles%\ScreenTimeGuardian\ScreenTimeGuardian.LockScreen.exe" (
    echo ERROR: lock screen executable missing after copy. Nothing further is installed.
    pause
    exit /b 1
)

echo [3/6] Installing Windows Service...
sc create ScreenTimeGuardian binPath= "%ProgramFiles%\ScreenTimeGuardian\ScreenTimeGuardian.Service.exe" start= auto DisplayName= "ScreenTime Guardian"
sc description ScreenTimeGuardian "Enforces screen time limits for children. Monitors usage and locks computer during break periods."
sc failure ScreenTimeGuardian reset= 86400 actions= restart/60000/restart/120000/restart/300000

echo [4/6] Installing watchdog...
:: Failure actions above only fire when the service exits without reporting a
:: clean SERVICE_STOPPED, so an administrator-driven "net stop" is NOT caught.
:: This SYSTEM-account task re-starts the service every minute if it is not running.
schtasks /delete /tn "ScreenTimeGuardianWatchdog" /f >nul 2>&1
schtasks /create /tn "ScreenTimeGuardianWatchdog" /tr "sc start ScreenTimeGuardian" /sc daily /ri:01 /ru SYSTEM /f >nul 2>&1
if %errorlevel% neq 0 (
    echo WARNING: Watchdog task not installed - the service can be left stopped.
)

echo [5/6] Locking down settings and command queue...
:: The service (LocalSystem) writes config.json and the ipc queue; the child's
:: session must not be able to, or they can self-unlock or reset their own PIN.
:: SIDs, not names: SYSTEM/Administrators are localized on non-English Windows
:: and an unresolvable name makes icacls skip the grant silently.
:: /inheritance:r drops the inherited read/write entries for ordinary users.
mkdir "%ProgramData%\ScreenTimeGuardian" 2>nul
icacls "%ProgramData%\ScreenTimeGuardian" /inheritance:r /grant:r "*S-1-5-18:(OI)(CI)F" "*S-1-5-32-544:(OI)(CI)F" /t
if %errorlevel% neq 0 goto :acl_fail
:: Verify it took: no inherited ACE may remain on the root (locale-independent
:: check - the (I) flag is a literal, unlike account names).
for /f "delims=" %%A in ('icacls "%ProgramData%\ScreenTimeGuardian" ^| find /c "(I)"') do set "acl_inherited=%%A"
if not "%acl_inherited%"=="0" goto :acl_fail
echo      Settings folder locked to SYSTEM + Administrators only.
goto :acl_ok

:acl_fail
echo.
echo ERROR: could not lock down %ProgramData%\ScreenTimeGuardian.
echo Without the lockdown a child can read the parent PIN and queue their
echo own STG_UNLOCK command - the app would enforce nothing.
echo Fix: as Administrator run icacls on that folder granting only SIDs
echo *S-1-5-18 (SYSTEM) and *S-1-5-32-544 (Administrators), then re-run
echo this installer.
pause
exit /b 1

:acl_ok

echo [6/6] Starting service...
net start ScreenTimeGuardian

echo.
echo ============================================
echo  Installation complete!
echo.
echo  Service installed: ScreenTimeGuardian
echo  Config app: %ProgramFiles%\ScreenTimeGuardian\ScreenTimeGuardian.Config.exe
echo.
echo  IMPORTANT: Run the Config app to set your
echo  parent PIN and usage duration!
echo ============================================
echo.
pause
