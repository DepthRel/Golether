@echo off
rem Step 3 of the emulator setup: creates the virtual device ready to use, starts it and puts a shortcut on the desktop.
rem   create-emulator.bat [device name]
rem An existing device keeps its data; its settings are brought to the ones below. Needs no administrator rights.
rem
rem Why the settings are written here, the defaults of avdmanager do not work:
rem   hw.keyboard=yes   without it the keyboard of the computer types nothing;
rem   hw.gpu.*          without them the picture is drawn by the processor, the system lags and video has no OpenGL ES;
rem   hw.ramSize=4G     2 GB is too little for Android 16 with Google APIs;
rem   three buttons     the default gesture navigation shows no buttons and is awkward with a mouse.
setlocal
call "%~dp0android-env.bat"

set "AVD_NAME=%~1"
if not defined AVD_NAME set "AVD_NAME=%DEFAULT_AVD%"
rem A port of its own: the device is told from another emulator that may be running.
set "PORT=5580"
set "SERIAL=emulator-%PORT%"
set "AVD_DIR=%AVD_HOME%\%AVD_NAME%.avd"
set "CONFIG=%AVD_DIR%\config.ini"

if not exist "%EMULATOR%" (
    echo The emulator is not installed: run install-android-tools.bat first.
    goto :failed
)

echo == Virtual device %AVD_NAME%
if exist "%CONFIG%" (
    echo Already exists, its data is kept.
    goto :configure
)
call "%AVDMANAGER%" create avd -n "%AVD_NAME%" -k "%SYSTEM_IMAGE%" -d "%DEVICE_PROFILE%" < nul
if not exist "%CONFIG%" goto :failed

:configure
findstr /v /b /c:"hw.keyboard=" /c:"hw.gpu.enabled=" /c:"hw.gpu.mode=" /c:"hw.ramSize=" "%CONFIG%" > "%CONFIG%.new"
>> "%CONFIG%.new" echo hw.keyboard=yes
>> "%CONFIG%.new" echo hw.gpu.enabled=yes
>> "%CONFIG%.new" echo hw.gpu.mode=auto
>> "%CONFIG%.new" echo hw.ramSize=4G
move /y "%CONFIG%.new" "%CONFIG%" >nul
if errorlevel 1 goto :failed

echo.
echo == Starting the device (the first start takes a few minutes)
start "Android Emulator" /min "%EMULATOR%" -avd "%AVD_NAME%" -port %PORT% -no-snapshot -feature -QuickbootFileBacked
"%ADB%" -s %SERIAL% wait-for-device
set /a TRIES=0

:wait
set "BOOTED="
for /f "delims=" %%b in ('"%ADB%" -s %SERIAL% shell getprop sys.boot_completed 2^>nul') do set "BOOTED=%%b"
if "%BOOTED%"=="1" goto :booted
set /a TRIES+=1
if %TRIES% geq 120 (
    echo The device did not finish starting in 6 minutes.
    goto :failed
)
ping -n 4 127.0.0.1 >nul
goto :wait

:booted
echo.
echo == Three navigation buttons
"%ADB%" -s %SERIAL% shell cmd overlay enable-exclusive --category com.android.internal.systemui.navbar.threebutton
if errorlevel 1 goto :failed

echo.
echo == Shortcut on the desktop
powershell -NoProfile -ExecutionPolicy Bypass -Command "$link = Join-Path ([Environment]::GetFolderPath('Desktop')) ('Android Emulator (' + $env:AVD_NAME + ').lnk'); $shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($link); $shortcut.TargetPath = $env:EMULATOR; $shortcut.Arguments = '-avd ' + $env:AVD_NAME + ' -no-snapshot -feature -QuickbootFileBacked'; $shortcut.WorkingDirectory = Split-Path $env:EMULATOR; $shortcut.WindowStyle = 7; $shortcut.Save(); $link"
if errorlevel 1 goto :failed

echo.
echo Done: the device is running. Start it next time with the shortcut or start-emulator.bat
pause
exit /b 0

:failed
echo.
echo FAILED: see the message above.
pause
exit /b 1
