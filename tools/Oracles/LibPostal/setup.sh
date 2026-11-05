#!/usr/bin/env bash
# Installs libpostal from its released conda-forge binary package and downloads the data of its two address-parser models.
# Nothing here opens or builds libpostal sources: the package is a prebuilt binary, executed only.
#
#   usage: setup.sh [directory]        (default: $HOME/onecontact/oracles/libpostal)
#
# Result:
#   <dir>/mamba/envs/lp   libpostal 1.1.4 (conda-forge, MIT) with the `postal` Python binding and the `address_parser` program
#   <dir>/data-base       libpostal's own data (language classifier, address expansions, numex, transliteration, parser model v1.0.0)
#   <dir>/data-senzing    the same with the parser model replaced by Senzing's updated model (libpostal-data v1.2.0)
set -euo pipefail
dir=${1:-$HOME/onecontact/oracles/libpostal}
mkdir -p "$dir" && cd "$dir"
export MAMBA_ROOT_PREFIX=$dir/mamba
if [ ! -x mm/bin/micromamba ]; then
  curl -sSL https://micro.mamba.pm/api/micromamba/linux-64/latest -o mm.tar.bz2
  mkdir -p mm && tar -xjf mm.tar.bz2 -C mm bin/micromamba
fi
[ -d "$MAMBA_ROOT_PREFIX/envs/lp" ] || ./mm/bin/micromamba create -y -p "$MAMBA_ROOT_PREFIX/envs/lp" -c conda-forge --override-channels libpostal=1.1.4 postal python=3.12
export PATH=$MAMBA_ROOT_PREFIX/envs/lp/bin:$PATH
# README step "libpostal_data download all <datadir>": the model files of libpostal's own release.
[ -d data-base/address_parser ] || { mkdir -p data-base && libpostal_data download all "$dir/data-base"; }
# Senzing's published libpostal data model, version 1.2.0: only parser.tar.gz differs from the base data.
if [ ! -d data-senzing/address_parser ]; then
  cp -r data-base data-senzing
  curl -sSL https://public-read-libpostal-data.s3.amazonaws.com/v1.2.0/parser.tar.gz -o parser-senzing-1.2.0.tar.gz
  tar -xzf parser-senzing-1.2.0.tar.gz -C data-senzing
fi
sha256sum parser-senzing-1.2.0.tar.gz
( cd data-base/address_parser && sha256sum * ) | sed 's/^/base     /'
( cd data-senzing/address_parser && sha256sum * ) | sed 's/^/senzing  /'
