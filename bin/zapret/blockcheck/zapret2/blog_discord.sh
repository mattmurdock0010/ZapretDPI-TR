#!/bin/sh

EXEDIR="$(dirname "$0")"
EXEDIR="$(cd "$EXEDIR"; pwd)"

export DOMAINS="discord.com"
"$EXEDIR/blockcheck2.sh" 2>&1 | tee "$EXEDIR/../blockcheck2_discord.log"
unix2dos "$EXEDIR/../blockcheck2_discord.log"
