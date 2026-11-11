#!/usr/bin/env bash
# Stages the Railway build context for a release (cli-install design D2): the tagged sources (git archive HEAD), plus
# the CLI builds in cli-dist/. `railway up` skips gitignored files, and cli-dist/* is gitignored so that a local image
# has no CLI builds, so the staged copy drops those two .gitignore lines.
#
#   stage-deploy-context.sh <dist-dir> <version> <out-dir>
#
# <dist-dir> holds one folder per build as actions/download-artifact writes them: cli-<platform>/panopticon[.exe].
# Every supported platform must be there.
set -euo pipefail

if [ "$#" -ne 3 ]; then
    echo "usage: $0 <dist-dir> <version> <out-dir>" >&2
    exit 2
fi

dist=$1
version=$2
out=$3
platforms=(linux-x64 linux-arm64 osx-arm64 win-x64)

if ! [[ "$version" =~ ^[0-9]+\.[0-9]+\.[0-9]+([.-][0-9A-Za-z.-]+)?$ ]]; then
    echo "not a release version: '$version'" >&2
    exit 1
fi

if [ -e "$out" ] && [ -n "$(ls -A "$out")" ]; then
    echo "$out isn't empty" >&2
    exit 1
fi

mkdir -p "$out"
git archive HEAD | tar -x -C "$out"

for platform in "${platforms[@]}"; do
    file=panopticon
    case "$platform" in win-*) file=panopticon.exe ;; esac
    source="$dist/cli-$platform/$file"
    if [ ! -f "$source" ]; then
        echo "missing the $platform build: $source" >&2
        exit 1
    fi
    install -d "$out/cli-dist/$platform"
    install -m 755 "$source" "$out/cli-dist/$platform/$file"
done
printf '%s\n' "$version" > "$out/cli-dist/version"

# Let railway up upload the builds: drop the two lines that keep cli-dist/ out of git.
grep -v -x -e 'cli-dist/\*' -e '!cli-dist/.keep' "$out/.gitignore" > "$out/.gitignore.staged" || true
mv "$out/.gitignore.staged" "$out/.gitignore"

echo "Staged $out: $version, with ${platforms[*]}"
