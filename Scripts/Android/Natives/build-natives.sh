#!/bin/bash
# Builds the native components of the Android client inside the container of Scripts/Android/Natives/Dockerfile:
#   libmpv with FFmpeg and its dependencies, all under the LGPL or more permissive licenses (no GPL parts), and
#   libgstreamer_android.so with the plugins of cameras and voice, from the official GStreamer binaries.
# /work keeps the sources and the intermediate files between runs; /out receives Native/<rid>/ folders.
set -euo pipefail

MPV_ANDROID_COMMIT="${MPV_ANDROID_COMMIT:-d25d7c1eecaa2ee7ead1715bac3c7e19f2eddab9}"
GSTREAMER_VERSION="${GSTREAMER_VERSION:-1.28.7}"
ARCHS="${ARCHS:-arm64 armv7l x86_64}"
OUT=/out
WORK=/work

# mpv-android names an architecture its own way; .NET names it by runtime identifier, GStreamer by Android ABI.
rid_of() { case "$1" in arm64) echo android-arm64 ;; armv7l) echo android-arm ;; x86_64) echo android-x64 ;; esac; }
abi_of() { case "$1" in arm64) echo arm64-v8a ;; armv7l) echo armeabi-v7a ;; x86_64) echo x86_64 ;; esac; }
# The target of the compiler for the small libraries below.
target_of() { case "$1" in arm64) echo aarch64-linux-android26 ;; armv7l) echo armv7a-linux-androideabi26 ;; x86_64) echo x86_64-linux-android26 ;; esac; }
triple_of() { case "$1" in arm64) echo aarch64-linux-android ;; armv7l) echo arm-linux-androideabi ;; x86_64) echo x86_64-linux-android ;; esac; }

# ---------------------------------------------------------------------------------------------------------------------
# libmpv
# ---------------------------------------------------------------------------------------------------------------------
if [ ! -d "$WORK/mpv-android" ]; then
    git clone https://github.com/mpv-android/mpv-android.git "$WORK/mpv-android"
fi
cd "$WORK/mpv-android"
git fetch -q origin "$MPV_ANDROID_COMMIT" || true
git checkout -q -f "$MPV_ANDROID_COMMIT"

cd buildscripts
# A build folder of another Meson cannot be reused: the intermediate files go when the version of the image changes.
meson_version="$(meson --version)"
if [ "$(cat "$WORK/.meson-version" 2>/dev/null)" != "$meson_version" ]; then
    rm -rf prefix deps/*/_build_*
    echo "$meson_version" > "$WORK/.meson-version"
fi

# IN_CI pins FFmpeg to the release tag of depinfo.sh and skips the package installation (the image has them).
export IN_CI=1
./download.sh

# download.sh follows the main branches of these projects; a build must be repeatable, so each is pinned to a commit.
pin() {
    git -C "deps/$1" fetch -q origin "$2" || true
    git -C "deps/$1" checkout -q "$2"
    git -C "deps/$1" submodule update -q --init --recursive
}
pin dav1d c6f51c881d80b43830b7c1fff2ba0d48599e8073
pin libass fe25f50686b1a6babef2f9b703abead0e97e0fec
pin libplacebo 275f7dea27924cdd34ab2e6c20e448b6bc4fab65
pin mpv ac4f3f392907e6fc4b8b525fae236eb4e6b18cb5

# No GPL parts: FFmpeg without --enable-gpl (version3 stays: mbedTLS is Apache-2.0, so FFmpeg becomes LGPLv3),
# mpv with -Dgpl=false (LGPLv2.1+).
sed -i 's/--enable-{gpl,version3}/--enable-version3/' scripts/ffmpeg.sh
grep -q -- '--enable-version3' scripts/ffmpeg.sh
grep -q -- '-Dgpl=false' scripts/mpv.sh || sed -i 's/-Dlibmpv=true -Dcplayer=false/-Dlibmpv=true -Dcplayer=false -Dgpl=false/' scripts/mpv.sh
grep -q -- '-Dgpl=false' scripts/mpv.sh

ndk_dir="$(readlink -f sdk/android-ndk-*)"

for arch in $ARCHS; do
    ./buildall.sh --arch "$arch" mpv

    rid="$(rid_of "$arch")"
    mkdir -p "$OUT/$rid"
    prefix="prefix/$arch"
    cp -L "$prefix"/lib/*.so "$OUT/$rid/"
    cp "$ndk_dir/toolchains/llvm/prebuilt/linux-x86_64/sysroot/usr/lib/$(triple_of "$arch")/libc++_shared.so" "$OUT/$rid/"
done

# The configuration of FFmpeg and mpv proves the licence of the build.
{
    echo "mpv-android $MPV_ANDROID_COMMIT"
    for dep in deps/*/; do
        name="$(basename "$dep")"
        if [ -d "$dep/.git" ]; then
            echo "$name $(git -C "$dep" describe --tags --always 2>/dev/null) $(git -C "$dep" rev-parse HEAD)"
        else
            echo "$name (release archive, version in include/depinfo.sh)"
        fi
    done
    echo
    echo "FFmpeg license: $(grep -h '^#define FFMPEG_LICENSE' deps/ffmpeg/_build_arm64/config.h 2>/dev/null || echo unknown)"
    echo "mpv gpl option: $(python3 -c "import json; print([o[\"value\"] for o in json.load(open(\"deps/mpv/_build_arm64/meson-info/intro-buildoptions.json\")) if o[\"name\"] == \"gpl\"][0])" 2>/dev/null || echo unknown)"
} > "$OUT/LIBMPV-BUILDINFO.txt"

# ---------------------------------------------------------------------------------------------------------------------
# GStreamer
# ---------------------------------------------------------------------------------------------------------------------
gst_root="$WORK/gstreamer-$GSTREAMER_VERSION"
gst_archive="gstreamer-1.0-android-universal-$GSTREAMER_VERSION.tar.xz"
gst_url="https://gstreamer.freedesktop.org/data/pkg/android/$GSTREAMER_VERSION/$gst_archive"
if [ ! -d "$gst_root" ]; then
    cd "$WORK"
    wget -q "$gst_url" -O "$gst_archive"
    wget -q "$gst_url.sha256sum" -O "$gst_archive.sha256sum"
    sha256sum -c "$gst_archive.sha256sum"
    mkdir -p "$gst_root"
    tar -xJf "$gst_archive" -C "$gst_root"
    sha256sum "$gst_archive" > "$gst_root.sha256"
    rm "$gst_archive"
fi

# A minimal ndk-build project: GStreamer's makefile links the chosen plugins and their dependencies statically into
# one libgstreamer_android.so and generates the Java glue (org.freedesktop.gstreamer.GStreamer).
project="$WORK/gst-project"
rm -rf "$project"
mkdir -p "$project/jni" "$project/src"
cat > "$project/jni/Android.mk" <<'MAKE'
LOCAL_PATH := $(call my-dir)
include $(CLEAR_VARS)
LOCAL_MODULE := golether_dummy
LOCAL_SRC_FILES := dummy.c
LOCAL_SHARED_LIBRARIES := gstreamer_android
include $(BUILD_SHARED_LIBRARY)

GSTREAMER_ROOT := $(GSTREAMER_ROOT_ANDROID)/$(GSTREAMER_ABI)
GSTREAMER_NDK_BUILD_PATH := $(GSTREAMER_ROOT)/share/gst-android/ndk-build/
GSTREAMER_PLUGINS := coreelements app autodetect playback typefindfunctions \
    videoconvertscale videorate videofilter videotestsrc \
    audioconvert audioresample audiotestsrc volume level audiomixer \
    webrtc nice dtls srtp sctp rtp rtpmanager vpx opus webrtcdsp \
    androidmedia opensles
GSTREAMER_EXTRA_DEPS := gstreamer-webrtc-1.0 gstreamer-sdp-1.0 gstreamer-app-1.0 gstreamer-video-1.0 gstreamer-audio-1.0
include $(GSTREAMER_NDK_BUILD_PATH)/gstreamer-1.0.mk
MAKE
cat > "$project/jni/Application.mk" <<'MAKE'
APP_PLATFORM := android-26
APP_STL := c++_shared
MAKE
echo 'void golether_dummy(void) {}' > "$project/jni/dummy.c"

for arch in $ARCHS; do
    abi="$(abi_of "$arch")"
    case "$abi" in arm64-v8a) gst_abi=arm64 ;; armeabi-v7a) gst_abi=armv7 ;; x86_64) gst_abi=x86_64 ;; esac
    (cd "$project" && "$ndk_dir/ndk-build" -j"$(nproc)" APP_ABI="$abi" \
        GSTREAMER_ROOT_ANDROID="$gst_root" GSTREAMER_ABI="$gst_abi" GSTREAMER_JAVA_SRC_DIR=src)
    rid="$(rid_of "$arch")"
    mkdir -p "$OUT/$rid/gstreamer"
    cp "$project/libs/$abi/libgstreamer_android.so" "$OUT/$rid/gstreamer/"

    # The runtime of .NET on Android opens imports by their exact name and looks symbols up in that library and in what
    # it depends on. The imports name six libraries, as on desktop systems; here each is an empty library over the one
    # that holds everything (Gst.cs keeps the names).
    for shim in glib gobject gstreamer gstapp gstsdp gstwebrtc; do
        "$ndk_dir/toolchains/llvm/prebuilt/linux-x86_64/bin/clang" --target="$(target_of "$arch")" -shared \
            -Wl,-soname,"libgolether-$shim.so" -o "$OUT/$rid/gstreamer/libgolether-$shim.so" "$project/jni/dummy.c" \
            -L"$project/libs/$abi" -Wl,--no-as-needed -lgstreamer_android
    done
done

# FFmpeg is built with --disable-stripping; the package needs no symbols (several times smaller).
strip_tool="$ndk_dir/toolchains/llvm/prebuilt/linux-x86_64/bin/llvm-strip"
for arch in $ARCHS; do
    find "$OUT/$(rid_of "$arch")" -name '*.so' -exec "$strip_tool" --strip-unneeded {} +
done

mkdir -p "$OUT/android/gstreamer-java"
cp -r "$project/src/." "$OUT/android/gstreamer-java/"
echo "GStreamer $GSTREAMER_VERSION ($gst_url)" > "$OUT/GSTREAMER-BUILDINFO.txt"
cat "$gst_root.sha256" >> "$OUT/GSTREAMER-BUILDINFO.txt"

echo "done"
