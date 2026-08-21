. "$TESTDIR/def.inc"

pktws_check_https_tls12()
{
	local testf=$1 domain="$2"
	local ok=0
	local PAYLOAD="--payload=tls_client_hello"

	# 1. Turkish ISP Discord priority: fake with SNI extension google.com + TTL 3..7
	for ttl in 3 4 5 6 7; do
		pktws_curl_test_update $testf $domain $PAYLOAD "--lua-desync=fake:blob=fake_default_tls:ip_ttl=$ttl:tls_mod=rnd,dupsid,sni=google.com" && ok=1
		[ $ok = 1 -a "$SCANLEVEL" != force ] && return 0
		pktws_curl_test_update $testf $domain $PAYLOAD "--lua-desync=fake:blob=fake_default_tls:ip_ttl=$ttl" && ok=1
		[ $ok = 1 -a "$SCANLEVEL" != force ] && return 0
	done

	# 2. Fake + multisplit (pos=sniext+1, pos=host+1, pos=midsld, pos=2)
	for ttl in 3 4 5 6; do
		pktws_curl_test_update $testf $domain $PAYLOAD "--lua-desync=fake:blob=fake_default_tls:ip_ttl=$ttl:tls_mod=rnd,dupsid,sni=google.com" "--lua-desync=multisplit:pos=sniext+1" && ok=1
		[ $ok = 1 -a "$SCANLEVEL" != force ] && return 0
		pktws_curl_test_update $testf $domain $PAYLOAD "--lua-desync=fake:blob=fake_default_tls:ip_ttl=$ttl:tls_mod=rnd,dupsid,sni=google.com" "--lua-desync=multisplit:pos=midsld" && ok=1
		[ $ok = 1 -a "$SCANLEVEL" != force ] && return 0
		pktws_curl_test_update $testf $domain $PAYLOAD "--lua-desync=fake:blob=fake_default_tls:ip_ttl=$ttl:tls_mod=rnd,dupsid,sni=google.com" "--lua-desync=multisplit:pos=2" && ok=1
		[ $ok = 1 -a "$SCANLEVEL" != force ] && return 0
	done

	# 3. Syndata
	for ttl in 3 4 5 6; do
		pktws_curl_test_update $testf $domain $PAYLOAD "--lua-desync=syndata:blob=fake_default_tls:ip_ttl=$ttl:tls_mod=rnd,dupsid,sni=google.com" && ok=1
		[ $ok = 1 -a "$SCANLEVEL" != force ] && return 0
	done

	# 4. Standard multisplit without fake
	pktws_curl_test_update $testf $domain $PAYLOAD "--lua-desync=multisplit:pos=sniext+1" && ok=1
	[ $ok = 1 -a "$SCANLEVEL" != force ] && return 0
	pktws_curl_test_update $testf $domain $PAYLOAD "--lua-desync=multisplit:pos=midsld" && ok=1
	[ $ok = 1 -a "$SCANLEVEL" != force ] && return 0
	pktws_curl_test_update $testf $domain $PAYLOAD "--lua-desync=multisplit:pos=2" && ok=1
	[ $ok = 1 -a "$SCANLEVEL" != force ] && return 0

	return 0
}

pktws_check_https_tls13()
{
	pktws_check_https_tls12 "$1" "$2"
}
