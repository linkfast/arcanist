#!/bin/sh
# The API of Arcanist's window in DSM (index.html), for DSM administrators only:
#   GET  ?action=status        the server's state, its port, the setup code until set up, and the shared folders
#                              with whether Arcanist (sc-arcanist) can read them.
#   GET  ?action=logs          the log files: Arcanist's (logs/arcanist-<date>.log, newest first) and the package's
#                              output (arcanist.log: the supervisor and what Arcanist writes to its console).
#   GET  ?action=log&file=F&offset=N   a log's bytes from N (without it, its last 128 KB), at most 512 KB, as text;
#                              X-Log-Offset/X-Log-End say which bytes they are and X-Log-File which file (F "latest":
#                              Arcanist's newest). An offset past the end (the file was trimmed) starts from 0.
#   POST action=port&port=N    changes the port and starts the server again on it.
#   POST action=restart        starts the server again.
#   POST ?action=environment&enabled=0|1   the test mode: whether it is on, and its variables as the body
#                              (text, one KEY=value per line; "Arcanist:UltimateCloud:ApiUrl" is saved as
#                              Arcanist__UltimateCloud__ApiUrl). Saves them and starts the server again if it runs.
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

test_mode ()
{
    [ "$(sed -n 's/^TEST_MODE=\(.*\)$/\1/p' "${VAR}/package.conf" 2>/dev/null | tail -n 1)" = "1" ] && echo true || echo false
}

# package.conf, which bin/arcanist-run reads on every start: $1 the port, $2 the test mode (true or false).
write_conf ()
{
    mode=0
    [ "$2" = true ] && mode=1
    printf 'PORT=%s\nTEST_MODE=%s\n' "$1" "${mode}" > "${VAR}/package.conf.new" 2>/dev/null \
        && mv "${VAR}/package.conf.new" "${VAR}/package.conf"
}

# The test mode's variables as a JSON array of {"key", "value"}.
variables_json ()
{
    out=""
    if [ -f "${VAR}/environment" ]; then
        while IFS= read -r line || [ -n "${line}" ]; do
            case "${line}" in *=*) ;; *) continue ;; esac
            out="${out:+${out},}{\"key\":$(json "${line%%=*}"),\"value\":$(json "${line#*=}")}"
        done < "${VAR}/environment"
    fi
    echo "[${out}]"
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

is_running ()
{
    pid="$(runtime_value processId)"
    if [ -n "${pid}" ] && kill -0 "${pid}" 2>/dev/null; then
        echo true
    elif [ -n "${pid}" ] && [ -d "/proc/${pid}" ]; then
        # Another user's process: kill -0 is refused, but it exists.
        echo true
    else
        echo false
    fi
}

status ()
{
    running="$(is_running)"

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

    # Sound cards' outputs (for server audio), and whether the service user may use them: a package cannot add its
    # user to the audio group on DSM 7.
    outputs=0
    usable=true
    for pcm in /dev/snd/pcmC*D*p; do
        [ -e "${pcm}" ] || continue
        outputs=$((outputs + 1))
        case "$(id -un 2>/dev/null)" in
            "${SERVICE_USER}") [ -r "${pcm}" ] && [ -w "${pcm}" ] || usable=false ;;
            root) su "${SERVICE_USER}" -s /bin/sh -c 'test -r "$1" && test -w "$1"' -- "${pcm}" 2>/dev/null || usable=false ;;
            *) usable=null ;;
        esac
    done
    [ "${outputs}" -gt 0 ] || usable=null

    respond "200 OK" "{\"soundOutputs\":${outputs},\"soundUsable\":${usable},\"user\":$(json "${user}"),\"cgiUser\":$(json "$(id -un 2>/dev/null)"),\"running\":${running},\"version\":$(json "$(runtime_value version)"),\"startedAt\":$(json "$(runtime_value startedAt)"),\"port\":$(configured_port),\"setupCompleted\":${setup_completed},\"setupCode\":${code},\"dataDirectory\":$(json "${VAR}"),\"dataBytes\":$(( ${data_kb:-0} * 1024 )),\"serviceUser\":$(json "${SERVICE_USER}"),\"dotnet\":${dotnet},\"ffmpeg\":${ffmpeg},\"testMode\":$(test_mode),\"variables\":$(variables_json),\"shares\":[${shares}]}"
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

query_value ()
{
    printf '%s' "${QUERY_STRING:-}" | tr '&' '\n' | sed -n "s/^$1=//p" | head -n 1
}

# Checks the test mode's variables (the request's body) and saves them to "environment", which bin/arcanist-run
# reads. Names may be written as settings (with ":"); those the package relies on are refused.
save_environment ()
{
    new="${VAR}/environment.new"
    if ! ( umask 077 && : > "${new}" ) 2>/dev/null; then
        respond "500 Internal Server Error" '{"error":"cannot_write"}'
    fi
    n=0
    while IFS= read -r line || [ -n "${line}" ]; do
        n=$((n + 1))
        line="$(printf '%s' "${line}" | tr -d '\r')"
        case "${line}" in ''|\#*) continue ;; esac
        key=""
        case "${line}" in
            *=*) key="$(printf '%s' "${line%%=*}" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//' -e 's/:/__/g')" ;;
        esac
        case "${key}" in
            ''|[0-9]*|*[!A-Za-z0-9_]*)
                rm -f "${new}"
                respond "400 Bad Request" "{\"error\":\"invalid_variable\",\"line\":${n}}"
                ;;
        esac
        case "$(printf '%s' "${key}" | tr 'a-z' 'A-Z')" in
            PATH|HOME|USER|SHELL|LD_*|DOTNET_ROOT|ARCANIST_DATA_DIR|ARCANIST_HOME|ARCANIST__DATADIR|URLS|ASPNETCORE_URLS|ARCANIST__HOSTING__*)
                rm -f "${new}"
                respond "400 Bad Request" "{\"error\":\"reserved_variable\",\"line\":${n},\"key\":$(json "${key}")}"
                ;;
        esac
        value="$(printf '%s' "${line#*=}" | sed -e 's/^[[:space:]]*//' -e 's/[[:space:]]*$//')"
        printf '%s=%s\n' "${key}" "${value}" >> "${new}"
    done <<EOF
${body}
EOF
    mv "${new}" "${VAR}/environment" || respond "500 Internal Server Error" '{"error":"cannot_write"}'
}

# ── Logs ─────────────────────────────────────────────────────────────────────

# A log's path under VAR from its name: Arcanist's (arcanist-<date>.log, or "latest" for the newest) or the package's
# output (arcanist.log). Nothing else can be read.
log_path ()
{
    case "$1" in
        latest)
            name="$(ls -1 "${VAR}/logs" 2>/dev/null | grep -E '^arcanist-[0-9]{8}\.log$' | sort | tail -n 1)"
            [ -n "${name}" ] && echo "logs/${name}"
            ;;
        arcanist.log)
            echo "arcanist.log"
            ;;
        arcanist-[0-9][0-9][0-9][0-9][0-9][0-9][0-9][0-9].log)
            echo "logs/$1"
            ;;
    esac
}

list_logs ()
{
    out=""
    for name in $(ls -1 "${VAR}/logs" 2>/dev/null | grep -E '^arcanist-[0-9]{8}\.log$' | sort -r) arcanist.log; do
        path="${VAR}/$(log_path "${name}")"
        [ -f "${path}" ] || continue
        size="$(wc -c < "${path}" 2>/dev/null | tr -cd '0-9')"
        modified="$(date -r "${path}" +%s 2>/dev/null | tr -cd '0-9')"
        kind=server
        [ "${name}" = arcanist.log ] && kind=package
        out="${out:+${out},}{\"file\":$(json "${name}"),\"kind\":\"${kind}\",\"size\":${size:-0},\"modified\":${modified:-0}}"
    done
    respond "200 OK" "{\"logs\":[${out}]}"
}

# Sends a log's bytes as text, with which bytes they are in the headers.
send_log ()
{
    name="$(query_value file)"
    rel="$(log_path "${name:-latest}")"
    path="${VAR}/${rel}"
    if [ -z "${rel}" ] || [ ! -f "${path}" ]; then
        respond "404 Not Found" '{"error":"log_not_found"}'
    fi
    if [ ! -r "${path}" ]; then
        respond "403 Forbidden" '{"error":"cannot_read"}'
    fi
    size="$(wc -c < "${path}" | tr -cd '0-9')"
    size="${size:-0}"
    offset="$(query_value offset | tr -cd '0-9')"
    if [ -z "${offset}" ]; then
        start=$(( size > 131072 ? size - 131072 : 0 ))
    elif [ "${offset}" -gt "${size}" ]; then
        start=0
    else
        start="${offset}"
    fi
    end="${size}"
    [ $(( end - start )) -gt 524288 ] && end=$(( start + 524288 ))
    printf 'Status: 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\nCache-Control: no-store\r\nX-Log-File: %s\r\nX-Log-Offset: %s\r\nX-Log-End: %s\r\nX-Log-Size: %s\r\n\r\n' \
        "${rel##*/}" "${start}" "${end}" "${size}"
    if [ "${end}" -gt "${start}" ]; then
        tail -c +$(( start + 1 )) "${path}" | head -c $(( end - start ))
    fi
    exit 0
}

case "${REQUEST_METHOD}" in
    GET)
        case "$(query_value action)" in
            logs) list_logs ;;
            log) send_log ;;
            *) status ;;
        esac
        ;;
    POST)
        if [ "${HTTP_X_ARCANIST_WINDOW}" != "1" ]; then
            respond "403 Forbidden" '{"error":"missing_header"}'
        fi
        length="$(printf '%s' "${CONTENT_LENGTH:-0}" | tr -cd '0-9')"
        if [ "${length:-0}" -gt 65536 ]; then
            respond "413 Payload Too Large" '{"error":"too_large"}'
        fi
        body="$(head -c "${length:-0}")"
        action="$(query_value action)"
        [ -n "${action}" ] || action="$(form_value action)"
        case "${action}" in
            environment)
                save_environment
                enabled=false
                [ "$(query_value enabled)" = "1" ] && enabled=true
                if ! write_conf "$(configured_port)" "${enabled}"; then
                    respond "500 Internal Server Error" '{"error":"cannot_write"}'
                fi
                if [ "$(is_running)" = true ]; then
                    restart_server
                    respond "200 OK" "{\"testMode\":${enabled},\"restarted\":true}"
                fi
                respond "200 OK" "{\"testMode\":${enabled},\"restarted\":false}"
                ;;
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
                if ! write_conf "${port}" "$(test_mode)"; then
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
