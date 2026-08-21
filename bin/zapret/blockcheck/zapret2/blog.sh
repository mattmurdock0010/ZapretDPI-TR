#!/bin/sh

export PATH="/bin:/usr/bin:/cygdrive/c/Windows/System32:$PATH"
export BATCH=1
export TEST_DEFAULT=standard
export DOMAINS_DEFAULT="discord.com rutracker.org"
export IPVS=4
export ENABLE_HTTP=0
export ENABLE_HTTPS_TLS12=1
export ENABLE_HTTPS_TLS13=1
export CURL_MAX_TIME=2
export CURL_MAX_TIME_DOH=2

EXEDIR="$(dirname "$0")"
EXEDIR="$(cd "$EXEDIR"; pwd)"

"$EXEDIR/blockcheck2.sh" 2>&1 | tee "$EXEDIR/../blockcheck2.log"
unix2dos "$EXEDIR/../blockcheck2.log"
