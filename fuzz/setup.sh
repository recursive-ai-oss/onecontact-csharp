#!/usr/bin/env bash
# Installs the fuzzing engine into a user-local folder: AFL++ (built from its release archive with the system C compiler) and the
# SharpFuzz instrumentation tool (a .NET tool). Nothing is installed system-wide and nothing needs root.
#
#   fuzz/setup.sh            install, or confirm an existing install
#
# Where things go (ONECONTACT_FUZZ, default ~/onecontact/fuzz):
#   afl/        the AFL++ build (afl-fuzz)
#   tools/      the sharpfuzz command
#   work/       builds, seed corpora, AFL output, results (made by run.sh)
set -euo pipefail

AFL_VERSION="v4.35c"
AFL_URL="https://github.com/AFLplusplus/AFLplusplus/archive/refs/tags/${AFL_VERSION}.tar.gz"
SHARPFUZZ_TOOL_VERSION="2.3.0"

FUZZ_HOME="${ONECONTACT_FUZZ:-$HOME/onecontact/fuzz}"
mkdir -p "$FUZZ_HOME"

if [ ! -x "$FUZZ_HOME/afl/afl-fuzz" ]; then
  # AFL++ is used as a program: fuzz/README.md says why this is not a source dependency of ours.
  mkdir -p "$FUZZ_HOME/src"
  curl -fsSL -o "$FUZZ_HOME/src/aflpp.tar.gz" "$AFL_URL"
  sha256sum "$FUZZ_HOME/src/aflpp.tar.gz" | tee "$FUZZ_HOME/src/aflpp.sha256"
  tar -xzf "$FUZZ_HOME/src/aflpp.tar.gz" -C "$FUZZ_HOME/src"
  build_dir="$(find "$FUZZ_HOME/src" -maxdepth 1 -type d -name 'AFLplusplus-*' | head -n 1)"
  # Only the fuzzer itself is needed: .NET code is instrumented by SharpFuzz, not by a compiler plugin.
  make -C "$build_dir" -j4 afl-fuzz afl-showmap NO_PYTHON=1 >"$FUZZ_HOME/src/build.log" 2>&1 \
    || { tail -n 30 "$FUZZ_HOME/src/build.log"; exit 1; }
  mkdir -p "$FUZZ_HOME/afl"
  cp "$build_dir/afl-fuzz" "$build_dir/afl-showmap" "$FUZZ_HOME/afl/"
  echo "$AFL_VERSION" >"$FUZZ_HOME/afl/VERSION"
fi

if [ ! -x "$FUZZ_HOME/tools/sharpfuzz" ]; then
  dotnet tool install SharpFuzz.CommandLine --version "$SHARPFUZZ_TOOL_VERSION" --tool-path "$FUZZ_HOME/tools"
fi

echo "AFL++ $(cat "$FUZZ_HOME/afl/VERSION") and SharpFuzz.CommandLine $SHARPFUZZ_TOOL_VERSION are in $FUZZ_HOME"
