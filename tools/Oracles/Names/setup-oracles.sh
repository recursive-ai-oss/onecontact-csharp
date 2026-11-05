#!/usr/bin/env bash
# Installs the pinned Python name oracles (python-nameparser, probablepeople, cleanco) from PyPI into a virtual environment.
# They are only executed afterwards; their installed files are never read.
#
#   usage: setup-oracles.sh [venv directory]      (default: $HOME/onecontact/oracles/names-venv)
set -euo pipefail
venv=${1:-$HOME/onecontact/oracles/names-venv}
here=$(cd "$(dirname "$0")" && pwd)
uv venv --python 3.13 "$venv"
# nameparser 2.8.2, probablepeople 0.5.6, cleanco 2.3; the wheel hashes of the three are listed in requirements.txt.
uv pip install --python "$venv/bin/python" --require-hashes --no-deps -r "$here/requirements-main.txt"
uv pip install --python "$venv/bin/python" -r "$here/requirements-deps.txt"
"$venv/bin/python" -c "from importlib import metadata; print({p: metadata.version(p) for p in ('nameparser','probablepeople','cleanco')})"
