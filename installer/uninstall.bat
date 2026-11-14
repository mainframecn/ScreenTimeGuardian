@echo off
echo ============================================
echo  ScreenTime Guardian - Uninstall Script
echo  Run as Administrator!
echo ============================================
echo.

:: Check for admin rights
net session >nul 2>&1
if %errorlevel% neq 0 (
    echo ERROR: This script must be run as Administrator!
    echo Right-click and select "Run as administrator"
    pause
    exit /b 1
)

echo [1/5] Stopping service...
:: Remove the watchdog first, otherwise it re-starts the service after we stop it.
schtasks /delete /tn "ScreenTimeGuardianWatchdog" /f 2>nul
net stop ScreenTimeGuardian 2>nul

echo [2/5] Removing service...
sc delete ScreenTimeGuardian 2>nul

echo [3/5] Removing legacy auto-start entry...
:: Only older installations wrote this; the service auto-starts on its own.
reg delete "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\Run" /v "ScreenTimeGuardian" /f 2>nul

echo [4/5] Cleaning up files...
rmdir /S /Q "%ProgramFiles%\ScreenTimeGuardian" 2>nul
rmdir /S /Q "%ProgramData%\ScreenTimeGuardian" 2>nul

echo [5/5] Restoring policy...
:: The service writes these under HKLM, because it runs as the LocalSystem account.
:: HKLM is also where a pre-fix installation may have left values under HKCU.
reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableTaskMgr" /f 2>nul
reg delete "HKLM\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v "NoClose" /f 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\System" /v "DisableTaskMgr" /f 2>nul
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Policies\Explorer" /v "NoClose" /f 2>nul

echo.
echo ============================================
echo  Uninstall complete!
echo  The computer has been restored to normal.
echo ============================================
echo.
pause
