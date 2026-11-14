@echo off
echo ============================================
echo  ScreenTime Guardian - development run
echo  Nothing is registered as a Windows service here, and no scheduled task
echo  is created. The service runs in this console, which is also how you test
echo  whether the lock screen is allowed to draw a window in a desktop session.
echo ============================================
echo.

cd /d "%~dp0.."

echo [1/3] Publishing all three programs into one folder...
rmdir /s /q "publish\devtest" 2>nul

dotnet publish src\ScreenTimeGuardian.Service\ScreenTimeGuardian.Service.csproj -c Debug -o "publish\devtest" || goto :fail
dotnet publish src\ScreenTimeGuardian.LockScreen\ScreenTimeGuardian.LockScreen.csproj -c Debug -o "publish\devtest" || goto :fail
dotnet publish src\ScreenTimeGuardian.Config\ScreenTimeGuardian.Config.csproj -c Debug -o "publish\devtest" || goto :fail

if not exist "publish\devtest\ScreenTimeGuardian.LockScreen.exe" (
    echo ERROR: lock screen executable is not next to the service. Stop here.
    goto :end
)

echo.
echo [2/3] Set the settings before starting - the service loads them once.
echo   Config app: %CD%\publish\devtest\ScreenTimeGuardian.Config.exe
echo   Settings file: %ProgramData%\ScreenTimeGuardian\config.json
echo   For a quick test put UsageDurationMinutes on 2 or 3.
echo.
pause

echo.
echo [3/3] Starting the service in this console. Ctrl+C to stop.
echo   Lines worth watching for:
echo     "Manual lock triggered" / "Lock screen activated"   - the window drew
echo     "Lock screen exited immediately with code N"        - it could not draw
echo     "LockScreen executable not found at ..."            - wrong folder
echo.
echo NOTE: a break writes HKLM policy values on THIS machine, and hides Task
echo NOTE: Manager and the shutdown menu for the length of the break. Run this
echo NOTE: from an administrator console, or stop the service before a break.
echo.
"publish\devtest\ScreenTimeGuardian.Service.exe"

echo.
echo Service stopped. Protection is cleared at the next start.

:end
exit /b 0

:fail
echo.
echo ERROR: build failed. Nothing was started.
pause
exit /b 1
