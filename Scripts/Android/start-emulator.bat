@echo off
rem Starts the virtual device: start-emulator.bat [device name]. The console window is minimized; closing it closes
rem the emulator. It always boots from scratch: a snapshot for a quick start and the file behind the memory of the
rem device would take 4 GB each and stay on the disk after the exit.
setlocal
call "%~dp0android-env.bat"

set "AVD_NAME=%~1"
if not defined AVD_NAME set "AVD_NAME=%DEFAULT_AVD%"

if not exist "%AVD_HOME%\%AVD_NAME%.avd\config.ini" (
    echo The device %AVD_NAME% does not exist: run create-emulator.bat first.
    pause
    exit /b 1
)

start "Android Emulator" /min "%EMULATOR%" -avd "%AVD_NAME%" -no-snapshot -feature -QuickbootFileBacked
