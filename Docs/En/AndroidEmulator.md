# Android emulator startup

How to prepare a Windows computer for running the Android client of Golether in an emulator. Android Studio is not
needed. The scripts bring the device to a working state by themselves: the keyboard, the navigation buttons and the
graphics acceleration work right after the installation.

## Quick start

You need Windows 10 or 11 (64-bit), virtualization enabled in the BIOS/UEFI settings and about 10 GB of free disk space.
Run the scripts from `Scripts/Android` in this order; a double click is enough:

| Step | Script | Administrator rights | What it does |
|---|---|---|---|
| 1 | `enable-windows-features.bat` | yes, it asks | Turns on the Windows Hypervisor Platform. **Restart Windows** afterwards |
| 2 | `install-android-tools.bat` | yes, it asks | Installs the .NET Android workload, the JDK, the Android SDK and the emulator: about 6 GB, downloads are checked by SHA-256 |
| 3 | `create-emulator.bat` | no | Creates the `Golether-API36` device, starts it and puts a shortcut on the desktop |

After that the emulator starts with the **Android Emulator (Golether-API36)** shortcut or with `start-emulator.bat`. It boots from scratch every time: a snapshot for a quick start would take 4 GB, rewritten on every exit. After a
failed exit the emulator asks whether to send a crash report and waits for the answer with the device switched off;
close that window.

From the command line the device shuts down cleanly with `adb shell reboot -p`; `adb emu kill` counts as a failed exit. The device has only a back camera: to try switching between two cameras, start the emulator once with `-camera-front emulated`.

Steps 2 and 3 can be run again: what is installed stays, and an existing device keeps its data.

## What goes where

| What | Where |
|---|---|
| .NET Android workload | The folder of .NET, `C:\Program Files\dotnet\packs`: a workload is always there |
| Microsoft OpenJDK 21 | `C:\Program Files\AndroidTools\jdk-21` |
| Android SDK, emulator, system image | `C:\Program Files\AndroidTools\sdk` |
| Virtual devices | `%USERPROFILE%\.android\avd`: the emulator writes there while it runs, so they cannot be in Program Files |

`JAVA_HOME` and `ANDROID_HOME` are set for all users. Restart terminals and Visual Studio to see them.

Two variables change the folders: `ANDROID_TOOLS_ROOT` for the tools and `ANDROID_AVD_HOME` for the devices. With
`ANDROID_TOOLS_ROOT` administrator rights are not needed (if the workload is installed), but then you set `JAVA_HOME`
and `ANDROID_HOME` yourself.

## The settings of the device

`avdmanager` creates a device that is inconvenient to work with, so `create-emulator.bat` changes it:

| Setting | Default | Set by the script | Why |
|---|---|---|---|
| `hw.keyboard` | `no` | `yes` | Otherwise the keyboard of the computer types nothing |
| `hw.gpu.enabled` | `no` | `yes` | Otherwise the processor draws the picture: the system lags and video has no OpenGL ES |
| `hw.gpu.mode` | `auto` | `auto` | The video card when possible, drawing by the processor on a computer without one |
| `hw.ramSize` | `2G` | `4G` | 2 GB is too little for Android 16 with Google APIs |
| Navigation | gestures | three buttons | There are no buttons on the screen with gestures, and gestures are awkward with a mouse |

The first four are lines in `config.ini` of the device. The navigation is set in the running system and stays there:

```powershell
adb shell cmd overlay enable-exclusive --category com.android.internal.systemui.navbar.threebutton
```

## Working with the emulator

- A minimized console window starts together with the emulator. Closing it closes the emulator.
- The computer is reachable from the emulator at `10.0.2.2`.
- The on-screen keyboard does not appear while the physical one is on: that is normal.
- The emulator shows neither the real hardware decoding of video nor the heat and the speed of a phone: those need a
  real phone connected over USB with USB debugging turned on.
- `sdkmanager` and `adb` are not in `PATH`. They are in `C:\Program Files\AndroidTools\sdk`, in
  `cmdline-tools\latest\bin` and `platform-tools`; `sdkmanager` writes to Program Files, so it needs a terminal run as
  administrator.

## Building and running the client

The Android projects are not part of the build of `Golether.slnx`: it must work without the Android workload. They are
built on their own, and they need the native components in `Native/android-*` (see `Native/README.md`):

```powershell
dotnet build Sources/Apps/Golether.Mobile.Android -t:Install
```

With `-p:GoletherDeviceChecks=true` the package also contains checks of what can be proved only on a device: data,
keys, TLS, libmpv, GStreamer. They are started by an extra of the activity and report to logcat with the tag
`GOLETHER-CHECK`:

```powershell
adb shell am start -n depthrel.golether/depthrel.golether.MainActivity --ez golether.checks true --ez golether.conference true
adb logcat -s GOLETHER-CHECK
```

## Problems

| What you see | What it means |
|---|---|
| `sdkmanager is not recognized` | It is not in `PATH`: use the full path or `install-android-tools.bat` |
| Access denied while `sdkmanager` installs | The terminal was not run as administrator |
| `WARNING: The SDK Manager CLI tool (sdkmanager) is deprecated` | A warning, not an error |
| `Error: Could not load devices from ...\devices.xml` from `avdmanager` | Harmless: the image has no such file, the device is created |
| The keyboard types nothing or the system lags | The device was made by hand: run `create-emulator.bat`, it keeps the data and fixes the settings |
| The emulator is very slow, step 1 was skipped | Run `enable-windows-features.bat` and restart Windows. Check with `emulator -accel-check` |
| `enable-windows-features.bat` fails | Virtualization is off in the BIOS/UEFI settings |
