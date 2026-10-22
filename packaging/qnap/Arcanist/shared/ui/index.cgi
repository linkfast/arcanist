#!/bin/sh
# Arcanist's page in QTS (App Center's Open button): whether it runs, its version and port, the setup code while
# setup is pending, the end of its log, and the button that opens Arcanist. Only QTS administrators see it: their
# session (the NAS_SID cookie) is checked with QTS's own authLogin.cgi.
CONF=/etc/config/qpkg.conf
QPKG_ROOT=$(/sbin/getcfg Arcanist Install_Path -f $CONF)
DATA="${QPKG_ROOT%/.qpkg/*}/.arcanist"

cookie() {
    echo "$HTTP_COOKIE" | tr ';' '\n' | sed -n "s/^ *$1=//p" | head -n 1 | tr -cd 'A-Za-z0-9._-'
}

deny() {
    printf 'Status: 403 Forbidden\r\nContent-Type: text/plain; charset=utf-8\r\n\r\n'
    echo "Sign in to QTS as an administrator to see Arcanist's page."
    exit 0
}

escape() {
    sed -e 's/&/\&amp;/g' -e 's/</\&lt;/g' -e 's/>/\&gt;/g'
}

sid=$(cookie NAS_SID)
[ -n "$sid" ] || deny
qts_port=$(/sbin/getcfg System "Web Access Port" -d 8080 -f /etc/config/uLinux.conf)
auth=$(curl -s -m 10 "http://127.0.0.1:${qts_port}/cgi-bin/authLogin.cgi?sid=${sid}" 2>/dev/null \
    || wget -q -T 10 -O - "http://127.0.0.1:${qts_port}/cgi-bin/authLogin.cgi?sid=${sid}" 2>/dev/null)
echo "$auth" | grep -Eq '<authPassed>(<!\[CDATA\[)?1' || deny
echo "$auth" | grep -Eq '<isAdmin>(<!\[CDATA\[)?1' || deny

PORT=8484
[ -r "$DATA/package.conf" ] && . "$DATA/package.conf"
if [ -f /var/run/arcanist.pid ] && kill -0 "$(cat /var/run/arcanist.pid)" 2>/dev/null; then
    state="running"
else
    state="stopped (start it in App Center)"
fi
version=$(sed -n 's/.*"version" *: *"\([^"]*\)".*/\1/p' "$DATA/runtime.json" 2>/dev/null | head -n 1)
code=$(cat "$DATA/setup-code.txt" 2>/dev/null | tr -cd 'A-Za-z0-9-')
log=$(ls -t "$DATA"/logs/arcanist-*.log 2>/dev/null | head -n 1)

printf 'Content-Type: text/html; charset=utf-8\r\nCache-Control: no-store\r\nX-Frame-Options: SAMEORIGIN\r\n\r\n'
cat <<EOF
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<title>Arcanist</title>
<style>
  :root { color-scheme: light dark; --accent: #7c3aed; }
  body { font: 15px/1.5 system-ui, sans-serif; margin: 0 auto; max-width: 960px; padding: 24px 16px; }
  h1 { margin: 0 0 4px; letter-spacing: .08em; color: var(--accent); }
  dl { display: grid; grid-template-columns: max-content 1fr; gap: 4px 16px; }
  dt { opacity: .7; }
  .code { font: 600 28px/1.2 ui-monospace, monospace; letter-spacing: .1em; }
  a.button { display: inline-block; background: var(--accent); color: #fff; padding: 8px 16px; border-radius: 6px; text-decoration: none; }
  pre { background: rgba(127,127,127,.12); padding: 12px; border-radius: 6px; overflow: auto; max-height: 420px; font-size: 12px; }
</style>
</head>
<body>
<h1>ARCANIST</h1>
<p>Your music server. Libraries, users and everything else are set up in Arcanist itself.</p>
<p><a class="button" id="open" href="#" target="_blank" rel="noopener">Open Arcanist</a></p>
<dl>
  <dt>State</dt><dd>$state</dd>
  <dt>Version</dt><dd>${version:-unknown}</dd>
  <dt>Port</dt><dd>$PORT</dd>
  <dt>Data</dt><dd>$(echo "$DATA" | escape)</dd>
</dl>
EOF
if [ -n "$code" ]; then
    cat <<EOF
<h2>Setup code</h2>
<p>Arcanist's setup wizard asks for it when it is opened from another device:</p>
<p class="code">$code</p>
EOF
fi
cat <<EOF
<h2>Music folders</h2>
<p>Arcanist runs as an administrator of the NAS, so it can read every shared folder: choose them in its setup wizard
(they are under <code>/share</code>, such as <code>/share/Music</code>).</p>
<h2>Log</h2>
<pre>$( { [ -n "$log" ] && tail -n 200 "$log"; tail -n 50 "$DATA/package.log" 2>/dev/null; } | escape)</pre>
<p>To change the port, set <code>PORT=</code> in <code>$(echo "$DATA" | escape)/package.conf</code> and restart Arcanist in App Center.</p>
<script>
  document.getElementById('open').href = location.protocol.replace('https', 'http') + '//' + location.hostname + ':$PORT/';
</script>
</body>
</html>
EOF
