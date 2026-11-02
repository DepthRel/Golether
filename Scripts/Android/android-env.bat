@echo off
rem The settings shared by the Android scripts; they call this file, it is not run by itself.
rem ANDROID_TOOLS_ROOT moves the tools to another folder, ANDROID_AVD_HOME moves the virtual devices.

if defined ANDROID_TOOLS_ROOT (set "TOOLS_ROOT=%ANDROID_TOOLS_ROOT%") else (set "TOOLS_ROOT=%ProgramFiles%\AndroidTools")
set "JDK_DIR=%TOOLS_ROOT%\jdk-21"
set "SDK_DIR=%TOOLS_ROOT%\sdk"

set "JDK_ZIP=microsoft-jdk-21.0.13.0.2-windows-x64.zip"
set "CMDLINE_TOOLS_ZIP=commandlinetools-win-15859902_latest.zip"
set "CMDLINE_TOOLS_SHA256=90ae805d20434428bffcb699c290860f19bb5f66a67e6b330067e3de801fb04a"

set "API_LEVEL=36"
set "BUILD_TOOLS=36.0.0"
set "SYSTEM_IMAGE=system-images;android-%API_LEVEL%;google_apis;x86_64"
set "DEFAULT_AVD=Golether-API%API_LEVEL%"
set "DEVICE_PROFILE=pixel_9"

if defined ANDROID_AVD_HOME (set "AVD_HOME=%ANDROID_AVD_HOME%") else (set "AVD_HOME=%USERPROFILE%\.android\avd")

set "SDKMANAGER=%SDK_DIR%\cmdline-tools\latest\bin\sdkmanager.bat"
set "AVDMANAGER=%SDK_DIR%\cmdline-tools\latest\bin\avdmanager.bat"
set "ADB=%SDK_DIR%\platform-tools\adb.exe"
set "EMULATOR=%SDK_DIR%\emulator\emulator.exe"

rem The tools of the SDK read these two; a terminal opened before the installation does not have them yet.
set "JAVA_HOME=%JDK_DIR%"
set "ANDROID_HOME=%SDK_DIR%"
