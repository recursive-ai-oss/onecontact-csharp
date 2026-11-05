#!/usr/bin/env bash
# Fetches the Census TIGER/Line 2025 files used for the regions the National Address Database does not cover (Puerto Rico, Guam,
# the U.S. Virgin Islands, the Northern Mariana Islands, American Samoa, Hawaii, Michigan, Mississippi, Nevada, New Hampshire):
#   - county address-range features (tl_2025_<state+county>_addrfeat.zip), from which only the attribute table is read;
#   - for American Samoa and the Northern Mariana Islands, which have no address ranges, the county edges files (street names);
#   - the 2020 ZIP Code tabulation area to place relationship file, which gives each ZIP its place names.
# Each file is checked against pins/tiger.pin. The pin was made by listing the county files of those states in the TIGER2025
# ADDRFEAT directory index and running this script with RECORD=1 (see fetch-register.sh); a different release needs a new pin.
exec "$(dirname "$0")/fetch-register.sh" "$(dirname "$0")/pins/tiger.pin" "$@"
