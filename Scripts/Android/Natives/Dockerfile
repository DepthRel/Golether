# The build environment of the native components of the Android client: libmpv (LGPL) and GStreamer.
# Built and run by Scripts/Android/build-natives.ps1; the result goes to Native/android-<abi>/.
FROM ubuntu:24.04

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update && apt-get install -y --no-install-recommends \
        autoconf automake build-essential ca-certificates curl default-jdk-headless file git gperf libtool \
        make nasm ninja-build patch pkg-config python3 python3-pip unzip wget xz-utils \
    && rm -rf /var/lib/apt/lists/*

# The meson of the distribution is older than mpv and libplacebo want.
RUN pip3 install --no-cache-dir --break-system-packages "meson==1.12.1"

WORKDIR /work
