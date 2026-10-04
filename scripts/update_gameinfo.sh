#!/usr/bin/env bash
# Run after every CS2 update: updates replace game/csgo/gameinfo.gi, which drops Metamod's search path
# (and with it CounterStrikeSharp and every plugin). Adds it back right after the Game_LowViolence line:
#
#     Game_LowViolence    csgo_lv // Perfect World content override
#     Game    csgo/addons/metamod
#
# Usage: run from the folder that contains serverfiles/ (e.g. ~):  ./scripts/update_gameinfo.sh

set -euo pipefail

file="$PWD/serverfiles/game/csgo/gameinfo.gi"

if [[ ! -f "$file" ]]; then
    echo "gameinfo.gi not found: $file" >&2
    exit 1
fi

# Already there (uncommented, any indentation / line ending)
if grep -Eq '^[[:space:]]*Game[[:space:]]+csgo/addons/metamod[[:space:]]*$' "$file"; then
    echo "Metamod is already in $file"
    exit 0
fi

tmp="$(mktemp)"
trap 'rm -f "$tmp"' EXIT

# Same indentation and line ending (CRLF or LF) as the Game_LowViolence line
if ! awk '
	{ print }
	!added && /^[ \t]*Game_LowViolence[ \t]+csgo_lv/ {
		match($0, /^[ \t]*/)
		indent = substr($0, 1, RLENGTH)
		cr = ($0 ~ /\r$/) ? "\r" : ""
		print indent "Game\tcsgo/addons/metamod" cr
		added = 1
	}
	END { exit added ? 0 : 1 }
' "$file" > "$tmp"; then
    echo "No 'Game_LowViolence csgo_lv' line in $file - add 'Game csgo/addons/metamod' by hand" >&2
    exit 1
fi

cp "$file" "$file.bak"
cat "$tmp" > "$file" # Keeps the file's owner and permissions
echo "Added Metamod to $file (backup: $file.bak)"
