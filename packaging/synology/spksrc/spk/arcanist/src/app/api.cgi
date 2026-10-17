#!/bin/sh
# The API of Arcanist's window in DSM (index.html), for DSM administrators only:
#   GET  ?action=status        the server's state, its port, the setup code until set up, and the shared folders
#                              with whether Arcanist (sc-arcanist) can read them.
#   POST action=port&port=N    changes the port and starts the server again on it.
#   POST action=restart        starts the server again.
# POST requests must carry the header X-Arcanist-Window: 1 (a form on another site cannot send it).
# Plain sh: DSM has no Python or jq by default.

PKG="arcanist"
VAR="/var/packages/${PKG}/var"
SERVICE_USER="sc-${PKG}"

respond ()
{
    # $1: HTTP status line, $2: JSON body
    printf 'Status: %s\r\nContent-Type: application/json; charset=utf-8\r\nCache-Control: no-store\r\n\r\n%s\n' "$1" "$2"
    exit 0
}

# A JSON string from a text: escapes backslashes, quotes and control characters.
json ()
{
    printf '"%s"' "$(printf '%s' "$1" | sed -e 's/\\/\\\\/g' -e 's/"/\\"/g' | tr -d '\000-\037')"
}

# ── Who asks ─────────────────────────────────────────────────────────────────

# DSM tells who is signed in from the session cookie and, with its protection against cross-site requests (on by
# default), the session's SynoToken, which the page sends in X-SYNO-TOKEN; authenticate.cgi reads it from the query.
token="$(printf '%s' "${HTTP_X_SYNO_TOKEN:-}" | tr -cd 'A-Za-z0-9._-')"
user="$(QUERY_STRING="SynoToken=${token}" HTTP_X_SYNO_TOKEN="${token}" /usr/syno/synoman/webman/modules/authenticate.cgi 2>/dev/null)"
if [ -z "${user}" ]; then
    respond "401 Unauthorized" '{"error":"not_signed_in"}'
fi
if ! grep -E '^administrators:' /etc/group | cut -d: -f4 | tr ',' '\n' | grep -qx "${user}"; then
    respond "403 Forbidden" '{"error":"not_admin"}'
fi

# ── Reading the state ────────────────────────────────────────────────────────

configured_port ()
{
    port="$(sed -n 's/^PORT=\([0-9][0-9]*\)$/\1/p' "${VAR}/package.conf" 2>/dev/null | tail -n 1)"
    echo "${port:-8484}"
}

# A value of runtime.json, which Arcanist writes while it runs ({"processId": …, "version": "…", …}).
runtime_value ()
{
    sed -n "s/.*\"$1\"[[:space:]]*:[[:space:]]*\"\{0,1\}\([^\",}]*\)\"\{0,1\}.*/\1/p" "${VAR}/runtime.json" 2>/dev/null | head -n 1
}

# Whether a port is being listened on (TCP, IPv4 or IPv6).
port_in_use ()
{
    hex="$(printf '%04X' "$1")"
    cat /proc/net/tcp /proc/net/tcp6 2>/dev/null | awk -v p=":${hex}" '$4 == "0A" && substr($2, length($2) - 4) == p { found = 1 } END { exit !found }'
}

# Whether the service user can read a folder. The window's CGI may run as the service user itself, or as root
# (which then asks as the service user); otherwise it cannot tell.
can_read ()
{
    me="$(id -un 2>/dev/null)"
    if [ "${me}" = "${SERVICE_USER}" ]; then
        [ -r "$1" ] && [ -x "$1" ] && echo true || echo false
    elif [ "${me}" = "root" ]; then
        su "${SERVICE_USER}" -s /bin/sh -c 'test -r "$1" && test -x "$1"' -- "$1" 2>/dev/null && echo true || echo false
    else
        echo null
    fi
}

status ()
{
    running=false
    pid="$(runtime_value processId)"
    if [ -n "${pid}" ] && kill -0 "${pid}" 2>/dev/null; then
        running=true
    elif [ -n "${pid}" ] && [ -d "/proc/${pid}" ]; then
        # Another user's process: kill -0 is refused, but it exists.
        running=true
    fi

    setup_completed=false
    if grep -q '"Completed"[[:space:]]*:[[:space:]]*true' "${VAR}/arcanist.json" 2>/dev/null; then
        setup_completed=true
    fi
    code="null"
    if [ "${setup_completed}" = false ] && [ -r "${VAR}/setup-code.txt" ]; then
        code="$(json "$(head -n 1 "${VAR}/setup-code.txt")")"
    fi

    # Shared folders: the top-level folders of each volume, but DSM's own (@…, #…, .…).
    shares=""
    for dir in /volume*/*/; do
        [ -d "${dir}" ] || continue
        dir="${dir%/}"
        name="${dir##*/}"
        case "${name}" in
            @*|\#*|.*|lost+found) continue ;;
        esac
        entry="{\"name\":$(json "${name}"),\"path\":$(json "${dir}"),\"readable\":$(can_read "${dir}")}"
        shares="${shares:+${shares},}${entry}"
    done

    data_kb="$(du -sk "${VAR}" 2>/dev/null | cut -f1)"

    # The SynoCommunity packages Arcanist depends on.
    dotnet=false
    [ -x /var/packages/dotnet10-runtime/target/share/dotnet/dotnet ] && dotnet=true
    ffmpeg=false
    [ -x /var/packages/ffmpeg8/target/bin/ffmpeg ] && ffmpeg=true

    respond "200 OK" "{\"user\":$(json "${user}"),\"cgiUser\":$(json "$(id -un 2>/dev/null)"),\"running\":${running},\"version\":$(json "$(runtime_value version)"),\"startedAt\":$(json "$(runtime_value startedAt)"),\"port\":$(configured_port),\"setupCompleted\":${setup_completed},\"setupCode\":${code},\"dataDirectory\":$(json "${VAR}"),\"dataBytes\":$(( ${data_kb:-0} * 1024 )),\"serviceUser\":$(json "${SERVICE_USER}"),\"dotnet\":${dotnet},\"ffmpeg\":${ffmpeg},\"shares\":[${shares}]}"
}

# ── Changes ──────────────────────────────────────────────────────────────────

# Asks the supervisor (bin/arcanist-run) to start the server again: it does when the server stops with the flag.
restart_server ()
{
    pid="$(runtime_value processId)"
    if [ -z "${pid}" ]; then
        respond "409 Conflict" '{"error":"not_running"}'
    fi
    touch "${VAR}/restart-requested" 2>/dev/null
    if ! kill -TERM "${pid}" 2>/dev/null; then
        rm -f "${VAR}/restart-requested"
        respond "409 Conflict" '{"error":"cannot_restart"}'
    fi
}

form_value ()
{
    printf '%s' "${body}" | tr '&' '\n' | sed -n "s/^$1=//p" | head -n 1
}

case "${REQUEST_METHOD}" in
    GET)
        status
        ;;
    POST)
        if [ "${HTTP_X_ARCANIST_WINDOW}" != "1" ]; then
            respond "403 Forbidden" '{"error":"missing_header"}'
        fi
        body="$(head -c "${CONTENT_LENGTH:-0}")"
        case "$(form_value action)" in
            port)
                port="$(form_value port)"
                case "${port}" in
                    ''|*[!0-9]*) respond "400 Bad Request" '{"error":"invalid_port"}' ;;
                esac
                if [ "${port}" -lt 1024 ] || [ "${port}" -gt 65535 ]; then
                    respond "400 Bad Request" '{"error":"invalid_port"}'
                fi
                if [ "${port}" != "$(configured_port)" ] && port_in_use "${port}"; then
                    respond "409 Conflict" '{"error":"port_in_use"}'
                fi
                if ! echo "PORT=${port}" > "${VAR}/package.conf.new" 2>/dev/null || ! mv "${VAR}/package.conf.new" "${VAR}/package.conf"; then
                    respond "500 Internal Server Error" '{"error":"cannot_write"}'
                fi
                restart_server
                respond "200 OK" "{\"port\":${port}}"
                ;;
            restart)
                restart_server
                respond "200 OK" '{}'
                ;;
            *)
                respond "400 Bad Request" '{"error":"unknown_action"}'
                ;;
        esac
        ;;
    *)
        respond "405 Method Not Allowed" '{"error":"method"}'
        ;;
esac
