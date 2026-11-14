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
:: Stop the service before copying: Windows refuses to overwrite a running
:: executable, and a half-copied install is how a machine ends up with a config
:: app but no service or lock screen.
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
schtasks /create /tn "ScreenTimeGuardianWatchdog" /tr "sc start ScreenTimeGuardian" /sc minutely /mo 1 /ru SYSTEM /f >nul 2>&1
if %errorlevel% neq 0 (
    echo WARNING: Watchdog task not installed - the service can be left stopped.
)

echo [5/6] Locking down settings and command queue...
:: The service (LocalSystem) writes config.json and the ipc queue; the child's
:: session must not be able to, or they can self-unlock or reset their own PIN.
:: /inheritance:r drops the inherited read/write entries for ordinary users.
mkdir "%ProgramData%\ScreenTimeGuardian" 2>nul
icacls "%ProgramData%\ScreenTimeGuardian" /inheritance:r /grant:r "SYSTEM:(OI,CI,F)" "Administrators:(OI,CI,F)" /t >nul
if %errorlevel% neq 0 (
    echo WARNING: could not lock down the settings folder.
    echo On a non-English Windows locale the account names SYSTEM and Administrators
    echo may need to be given as security identifiers instead. Until that is fixed,
    echo a child could edit the config or queue their own unlock command.
)

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
