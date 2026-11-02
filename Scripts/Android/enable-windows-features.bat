@echo off
rem Step 1 of the emulator setup: turns on the Windows Hypervisor Platform, which accelerates the Android emulator.
rem The full Hyper-V is not needed. Asks for administrator rights; Windows has to be restarted afterwards.
setlocal

net session >nul 2>&1
if not errorlevel 1 goto :elevated
echo Asking for administrator rights...
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b

:elevated
"%SystemRoot%\System32\dism.exe" /online /enable-feature /featurename:HypervisorPlatform /all /norestart
if errorlevel 3010 if not errorlevel 3011 goto :restart
if errorlevel 1 goto :failed

echo.
echo The Windows Hypervisor Platform is on. Next: install-android-tools.bat
pause
exit /b 0

:restart
echo.
echo The Windows Hypervisor Platform is on. Restart Windows, then run install-android-tools.bat
pause
exit /b 0

:failed
echo.
echo FAILED: the Windows Hypervisor Platform could not be turned on (see the message above).
echo Virtualization may be switched off in the BIOS/UEFI settings of the computer.
pause
exit /b 1
