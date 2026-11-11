#!/bin/sh
# Installs the Panopticon CLI @@VERSION@@ from @@SERVER_DISPLAY@@ (spec cli-distribution, design D3).
#
#   curl -fsSL @@SERVER_DISPLAY@@/install.sh | sh
#
# It installs to ~/.local/bin, or to PANOPTICON_INSTALL_DIR when that's set. It needs curl or wget, and sha256sum or
# shasum. The server fills in this script for its own release: every download comes from that server.
set -eu

server=@@SERVER@@

fail() {
    printf 'panopticon install: %s\n' "$1" >&2
    exit 1
}

# The platform. PANOPTICON_UNAME_S, PANOPTICON_UNAME_M and PANOPTICON_LIBC exist for Panopticon's own tests only.
os=${PANOPTICON_UNAME_S:-$(uname -s)}
arch=${PANOPTICON_UNAME_M:-$(uname -m)}
case "$os" in
    Linux) os_id=linux ;;
    Darwin) os_id=osx ;;
    *) fail "unsupported platform: $os $arch. The CLI runs on Linux (x64, arm64), macOS (Apple Silicon) and Windows (x64)." ;;
esac
case "$arch" in
    x86_64 | amd64) arch_id=x64 ;;
    aarch64 | arm64) arch_id=arm64 ;;
    *) fail "unsupported platform: $os $arch. The CLI runs on Linux (x64, arm64), macOS (Apple Silicon) and Windows (x64)." ;;
esac

if [ "$os_id" = linux ]; then
    libc=${PANOPTICON_LIBC:-}
    if [ -z "$libc" ]; then
        libc=glibc
        if ls /lib/ld-musl-* >/dev/null 2>&1 || { ldd --version 2>&1 | grep -qi musl; }; then
            libc=musl
        fi
    fi
    if [ "$libc" = musl ]; then
        fail "unsupported platform: musl Linux (such as Alpine). The CLI needs glibc Linux (2.35 or later)."
    fi
fi

platform="$os_id-$arch_id"
case "$platform" in
@@PLATFORM_CASES@@
    *) fail "unsupported platform: $os $arch ($platform). This server has CLI builds for: @@PLATFORM_LIST@@." ;;
esac

# Download next to where it's going, so the final move is a rename that can't leave a half-written file.
dir=${PANOPTICON_INSTALL_DIR:-$HOME/.local/bin}
mkdir -p "$dir" || fail "can't create $dir."
target="$dir/panopticon"
tmp="$dir/.panopticon-install-$$"
trap 'rm -f "$tmp"' EXIT
trap 'rm -f "$tmp"; exit 1' HUP INT TERM

if command -v curl >/dev/null 2>&1; then
    curl -fsSL -o "$tmp" "$url" || fail "couldn't download $url."
elif command -v wget >/dev/null 2>&1; then
    wget -q -O "$tmp" "$url" || fail "couldn't download $url."
else
    fail "this script needs curl or wget."
fi

if command -v sha256sum >/dev/null 2>&1; then
    actual=$(sha256sum "$tmp" | cut -d ' ' -f 1)
elif command -v shasum >/dev/null 2>&1; then
    actual=$(shasum -a 256 "$tmp" | cut -d ' ' -f 1)
else
    fail "this script needs sha256sum or shasum to check the download."
fi
if [ "$actual" != "$sha256" ]; then
    fail "checksum mismatch for $url: expected $sha256, got $actual. Nothing was installed."
fi

chmod 755 "$tmp"
# Run it before it replaces anything: a machine whose glibc is too old fails here, and keeps its old CLI.
if ! output=$("$tmp" --version 2>&1); then
    fail "the downloaded CLI doesn't run on this machine. Nothing was installed. It said: $output"
fi
mv -f "$tmp" "$target" || fail "can't write $target."
trap - EXIT HUP INT TERM

printf 'Installed panopticon %s to %s\n' "$output" "$target"
case ":$PATH:" in
    *":$dir:"*) ;;
    *)
        printf '\n%s is not on your PATH. Add it, e.g. in ~/.profile:\n' "$dir"
        # shellcheck disable=SC2016 # the hint prints a literal $PATH, for the reader's shell to expand
        printf '  export PATH="%s:$PATH"\n' "$dir"
        ;;
esac
printf '\nNext, log this machine in:\n  panopticon login --server %s\n' "$server"
