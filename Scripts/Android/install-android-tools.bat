@echo off
rem Step 2 of the emulator setup: installs what building and running the Android client needs:
rem   the .NET Android workload (always in the folder of .NET),
rem   Microsoft OpenJDK and the Android SDK (in C:\Program Files\AndroidTools, or in ANDROID_TOOLS_ROOT),
rem   the emulator and its system image.
rem Downloads are checked by SHA-256, what is already installed is left alone, the script can be run again.
rem Program Files needs administrator rights; a folder given in ANDROID_TOOLS_ROOT does not.
setlocal
call "%~dp0android-env.bat"

net session >nul 2>&1
if not errorlevel 1 goto :ready
if defined ANDROID_TOOLS_ROOT goto :ready
echo Asking for administrator rights...
powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
exit /b

:ready
set "CURL=%SystemRoot%\System32\curl.exe"
set "TAR=%SystemRoot%\System32\tar.exe"
set "WORK=%TEMP%\golether-android-tools-%RANDOM%"
mkdir "%WORK%" "%TOOLS_ROOT%" 2>nul

echo.
echo == .NET Android workload
dotnet workload list | findstr /b /c:"android " >nul
if not errorlevel 1 (
    echo Already installed.
    goto :jdk
)
net session >nul 2>&1
if errorlevel 1 (
    echo The workload needs administrator rights: run this script as administrator.
    goto :failed
)
dotnet workload install android
if errorlevel 1 goto :failed

:jdk
echo.
echo == Microsoft OpenJDK
if exist "%JDK_DIR%\bin\java.exe" (
    echo Already installed.
    goto :sdk
)
set "JDK_URL=https://aka.ms/download-jdk/%JDK_ZIP%"
"%CURL%" -fsSL --retry 3 -o "%WORK%\jdk.sha256" "%JDK_URL%.sha256sum.txt"
if errorlevel 1 goto :failed
set "JDK_SHA256="
for /f "usebackq tokens=1" %%h in ("%WORK%\jdk.sha256") do if not defined JDK_SHA256 set "JDK_SHA256=%%h"
call :fetch "%JDK_URL%" "%WORK%\jdk.zip" "%JDK_SHA256%"
if errorlevel 1 goto :failed
mkdir "%WORK%\jdk"
"%TAR%" -xf "%WORK%\jdk.zip" -C "%WORK%\jdk"
if errorlevel 1 goto :failed
set "UNPACKED="
for /d %%d in ("%WORK%\jdk\*") do set "UNPACKED=%%d"
if not defined UNPACKED goto :failed
call :place "%UNPACKED%" "%JDK_DIR%"
if errorlevel 1 goto :failed

:sdk
echo.
echo == Android SDK command-line tools
if exist "%SDKMANAGER%" (
    echo Already installed.
    goto :packages
)
call :fetch "https://dl.google.com/android/repository/%CMDLINE_TOOLS_ZIP%" "%WORK%\tools.zip" "%CMDLINE_TOOLS_SHA256%"
if errorlevel 1 goto :failed
mkdir "%WORK%\tools"
"%TAR%" -xf "%WORK%\tools.zip" -C "%WORK%\tools"
if errorlevel 1 goto :failed
call :place "%WORK%\tools\cmdline-tools" "%SDK_DIR%\cmdline-tools\latest"
if errorlevel 1 goto :failed

:packages
echo.
echo == Android SDK licenses
(for /l %%i in (1,1,30) do @echo y) > "%WORK%\yes.txt"
call "%SDKMANAGER%" "--sdk_root=%SDK_DIR%" --licenses < "%WORK%\yes.txt" >nul
if errorlevel 1 goto :failed

echo.
echo == Android SDK packages (the system image is large: this takes a while)
call "%SDKMANAGER%" "--sdk_root=%SDK_DIR%" "platform-tools" "platforms;android-%API_LEVEL%" "build-tools;%BUILD_TOOLS%" "emulator" "%SYSTEM_IMAGE%" < "%WORK%\yes.txt"
if errorlevel 1 goto :failed
if not exist "%EMULATOR%" goto :failed

echo.
echo == Environment variables
net session >nul 2>&1
if errorlevel 1 (
    echo Skipped without administrator rights. Set JAVA_HOME=%JDK_DIR% and ANDROID_HOME=%SDK_DIR% yourself.
    goto :done
)
setx /M JAVA_HOME "%JDK_DIR%" >nul
if errorlevel 1 goto :failed
setx /M ANDROID_HOME "%SDK_DIR%" >nul
if errorlevel 1 goto :failed
echo JAVA_HOME and ANDROID_HOME are set for all users.

:done
rmdir /s /q "%WORK%" 2>nul
echo.
echo Done. Restart terminals and Visual Studio to see the variables. Next: create-emulator.bat
pause
exit /b 0

:failed
rmdir /s /q "%WORK%" 2>nul
echo.
echo FAILED: see the message above.
pause
exit /b 1

rem Downloads a file and checks its SHA-256: URL, file, expected hash.
:fetch
echo Downloading %~1
"%CURL%" -fL --retry 3 --progress-bar -o "%~2" "%~1"
if errorlevel 1 exit /b 1
set "ACTUAL="
for /f "skip=1 delims=" %%h in ('certutil -hashfile "%~2" SHA256') do if not defined ACTUAL set "ACTUAL=%%h"
set "ACTUAL=%ACTUAL: =%"
if /i not "%ACTUAL%"=="%~3" (
    echo SHA-256 mismatch: expected %~3, got %ACTUAL%
    exit /b 1
)
echo SHA-256 is correct.
exit /b 0

rem Moves a folder to its place, on another drive too: robocopy reports success with a code below 8.
:place
robocopy "%~1" "%~2" /e /move /njh /njs /nfl /ndl /np >nul
if errorlevel 8 exit /b 1
exit /b 0
