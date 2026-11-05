#!/usr/bin/env bash
# Installs spaCy and its released English transformer pipeline (en_core_web_trf) into a virtual environment, for the extraction
# baselines. The packages are only executed afterwards; their installed files are never read. torch is the CPU build.
#
#   usage: setup-spacy.sh [venv directory]      (default: $HOME/onecontact/oracles/spacy-venv)
# Prints the installed versions and the SHA-256 of the model wheel, to record in the evaluation notes.
set -euo pipefail
venv=${1:-$HOME/onecontact/oracles/spacy-venv}
uv venv --python 3.13 "$venv"
uv pip install --python "$venv/bin/python" torch --index-url https://download.pytorch.org/whl/cpu
uv pip install --python "$venv/bin/python" "spacy==3.8.*" "spacy-transformers"
wheel_dir=$(dirname "$venv")/spacy-wheels
mkdir -p "$wheel_dir"
wheel="$wheel_dir/en_core_web_trf-3.8.0-py3-none-any.whl"
[ -f "$wheel" ] || curl -sSL -o "$wheel" "https://github.com/explosion/spacy-models/releases/download/en_core_web_trf-3.8.0/en_core_web_trf-3.8.0-py3-none-any.whl"
sha256sum "$wheel"
uv pip install --python "$venv/bin/python" "$wheel"
"$venv/bin/python" -c "from importlib import metadata; print({p: metadata.version(p) for p in ('spacy','spacy-transformers','torch','en-core-web-trf')})"
