#!/usr/bin/env bash
# Builds the reference address library (Java, "common" module) from its pinned release archive, to use as a black box.
#
#   usage: build-oracle.sh [destination]    (default: ~/onecontact/oracles/libaddressinput/<short commit>)
#
# The archive is unpacked and built by the project's documented step (the Gradle wrapper, as its common/README says).
# Nothing inside the unpacked tree is opened, listed or searched: the script only runs the build and keeps the jar it
# produces, then records the jar's SHA-256. If the build fails the script stops; nothing is edited to make it pass.
set -euo pipefail

commit=81eb9628382b07d371d8ea0b11badf7de3857fd5
dest=${1:-$HOME/onecontact/oracles/libaddressinput/${commit:0:8}}
jdk=${JAVA_HOME:-$HOME/onecontact/oracles/jdk/current}
export JAVA_HOME=$jdk PATH=$jdk/bin:$PATH

mkdir -p "$dest"
if [ ! -f "$dest/archive.tar.gz" ]; then
  curl --fail --silent --show-error --location --proto '=https' \
    --output "$dest/archive.tar.gz.part" "https://codeload.github.com/google/libaddressinput/tar.gz/$commit"
  mv "$dest/archive.tar.gz.part" "$dest/archive.tar.gz"
fi
sha256sum "$dest/archive.tar.gz" | cut -d' ' -f1 > "$dest/archive.sha256"

rm -rf "$dest/tree"
mkdir -p "$dest/tree"
tar -xzf "$dest/archive.tar.gz" -C "$dest/tree" --strip-components=1

# The release archive carries the wrapper scripts but not the wrapper's jar, so the documented tool is run directly:
# a plain Gradle 8.7 distribution (SHA-256 of gradle-8.7-bin.zip 544c35d6bd849ae8a5ed0bcea39ba677dc40f49df7d1835561582da2009b961d).
gradle=${GRADLE:-$HOME/onecontact/oracles/gradle/gradle-8.7/bin/gradle}
( cd "$dest/tree" && "$gradle" --no-daemon --console=plain :common:jar > "$dest/build.log" 2>&1 ) || {
  echo "build failed; see $dest/build.log" >&2
  tail -n 30 "$dest/build.log" >&2
  exit 1
}

# The jar's location is the one the project's docs give.
cp "$dest/tree/common/build/libs/common.jar" "$dest/common.jar"
sha256sum "$dest/common.jar" | cut -d' ' -f1 > "$dest/common.jar.sha256"
echo "common.jar sha256 $(cat "$dest/common.jar.sha256")"
