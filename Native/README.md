# Native components

Nothing has to be put here by hand. The `golether-components` utility (`Scripts/App/publish-win.ps1` runs it)
downloads the pinned versions, checks their SHA-256 and lays the files out:

```
Native/<RID>/libmpv-2.dll          the video component
Native/<RID>/gstreamer/...         the camera and voice component (only the needed plugins and their dependencies, ~37 MB)
```

A build of the UI project copies the content of `Native/<RID>/` to `native/` next to the application. The subfolders
are ignored by git.

To prepare the components for debugging:

```powershell
dotnet run --project Sources/Tools/Golether.Tools.Components -- fetch --output Native/win-x64 --cache artifacts/cache/components
```

When the components are neither in the package nor in `native/`, the application shows an "Install" button and puts
them into the data folder of the user (`components/`) without administrator rights.

| Component | Windows | Linux | macOS |
|---|---|---|---|
| Video (libmpv) | the `mpv-dev-*.7z` archive (shinchiro/mpv-winbuild-cmake), unpacked with the built-in `tar.exe` | a package of the distribution (`libmpv2`, `mpv-libs`); the application suggests the command | `brew install mpv` |
| Cameras and voice (GStreamer) | the official 1.28.7 installer: a silent installation into a temporary folder, the needed files are picked, the temporary installation is removed. A complete GStreamer that is already installed is used without a download | the `gstreamer1.0-plugins-{base,good,bad}` and `gstreamer1.0-nice` packages | `brew install gstreamer` |

An explicit path to libmpv can still be given with the `GOLETHER_LIBMPV` variable.
