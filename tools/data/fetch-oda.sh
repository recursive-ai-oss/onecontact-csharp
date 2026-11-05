#!/usr/bin/env bash
# Fetches Statistics Canada's Open Database of Addresses, version 1.0 (catalogue 46-26-0001, issue 2021001), ten provinces
# and territories as zipped CSV. Licence: Open Government Licence - Canada.
exec "$(dirname "$0")/fetch-register.sh" "$(dirname "$0")/pins/oda.pin" "$@"
