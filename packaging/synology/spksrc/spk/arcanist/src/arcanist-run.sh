#!/bin/sh
# Runs Arcanist on DSM and starts it again when it asks to be: after a factory reset or a server restore (it exits
# with code 75), and when the package's window asks for it to apply a new port or the test mode (the window leaves
# "restart-requested" and stops the server).
# Reads the port from package.conf on every start. DSM stops the package with TERM, which is passed on to Arcanist.
# In test mode (TEST_MODE=1 in package.conf, set from the window), the variables in "environment" (KEY=value lines)
# are given to Arcanist too: settings such as Arcanist__UltimateCloud__ApiUrl for services not published yet.
#
# Environment: ARCANIST_DATA_DIR (the package's var folder), ARCANIST_HOME (the server's folder) and DOTNET_ROOT (the
# .NET runtime that runs it; without it, the server's own launcher, for a self-contained build).

conf="${ARCANIST_DATA_DIR}/package.conf"
flag="${ARCANIST_DATA_DIR}/restart-requested"
environment="${ARCANIST_DATA_DIR}/environment"
child=""
stopping=""
# Starts the server asked for (code 75) in the last minute: a server that keeps asking is stopped instead of looping.
recent=""

trap 'stopping=1; [ -n "${child}" ] && kill -TERM "${child}" 2>/dev/null' TERM INT

# DSM writes the package's output (this script's and what the server writes to its console) to arcanist.log and
# clears it only when the package starts: over 10 MB, its oldest part is dropped. Everyone writes to it in append
# mode, so cutting it while they write is safe (a few lines may be lost).
package_log="${ARCANIST_DATA_DIR}/arcanist.log"
trim_package_log ()
{
    while :; do
        size="$(wc -c < "${package_log}" 2>/dev/null | tr -cd '0-9')"
        if [ "${size:-0}" -gt 10485760 ]; then
            tail -c 2097152 "${package_log}" > "${package_log}.trim" && cat "${package_log}.trim" > "${package_log}"
            rm -f "${package_log}.trim"
        fi
        sleep "${ARCANIST_LOG_TRIM_SECONDS:-60}"
    done
}
trim_package_log &
trimmer=$!
trap 'kill "${trimmer}" 2>/dev/null' EXIT

port_from_conf ()
{
    port="$(sed -n 's/^PORT=\([0-9][0-9]*\)$/\1/p' "${conf}" 2>/dev/null | tail -n 1)"
    if [ -z "${port}" ] || [ "${port}" -lt 1024 ] || [ "${port}" -gt 65535 ]; then
        port=8484
    fi
    echo "${port}"
}

test_mode ()
{
    [ "$(sed -n 's/^TEST_MODE=\(.*\)$/\1/p' "${conf}" 2>/dev/null | tail -n 1)" = "1" ]
}

# Exports the test mode's variables. Names are checked again (the window checks them when saving), and those this
# script and the package rely on are never replaced. Values are logged by name only: they may be secrets.
export_test_variables ()
{
    [ -f "${environment}" ] || return 0
    while IFS= read -r line || [ -n "${line}" ]; do
        case "${line}" in *=*) ;; *) continue ;; esac
        key="${line%%=*}"
        case "${key}" in ''|[0-9]*|*[!A-Za-z0-9_]*) continue ;; esac
        case "$(printf '%s' "${key}" | tr 'a-z' 'A-Z')" in
            PATH|HOME|USER|SHELL|LD_*|DOTNET_ROOT|ARCANIST_DATA_DIR|ARCANIST_HOME|ARCANIST__DATADIR|URLS|ASPNETCORE_URLS|ARCANIST__HOSTING__*) continue ;;
        esac
        export "${key}=${line#*=}"
        echo "$(date) Test mode: ${key}"
    done < "${environment}"
}

while :; do
    rm -f "${flag}"
    port="$(port_from_conf)"
    echo "$(date) Starting Arcanist on port ${port}"
    # In a subshell, so the test mode's variables never stay behind for the next start.
    (
        test_mode && export_test_variables
        if [ -n "${DOTNET_ROOT:-}" ]; then
            exec "${DOTNET_ROOT}/dotnet" "${ARCANIST_HOME}/arcanist.dll" --urls "http://*:${port}"
        else
            exec "${ARCANIST_HOME}/arcanist" --urls "http://*:${port}"
        fi
    ) &
    child=$!

    # wait returns early when a signal arrives: wait again until the server has really ended.
    wait "${child}"
    code=$?
    while kill -0 "${child}" 2>/dev/null; do
        wait "${child}"
        code=$?
    done
    child=""

    if [ -n "${stopping}" ]; then
        exit 0
    fi
    # Asked from the window (a new port, the test mode): an administrator's doing, so not counted.
    if [ -f "${flag}" ]; then
        echo "$(date) Starting Arcanist again, as asked from its window"
        sleep 1
        continue
    fi
    if [ "${code}" -eq 75 ]; then
        now="$(date +%s)"
        kept=""
        for at in ${recent}; do
            [ $((now - at)) -lt 60 ] && kept="${kept} ${at}"
        done
        recent="${kept} ${now}"
        if [ "$(echo ${recent} | wc -w)" -gt 5 ]; then
            echo "$(date) Arcanist asked to start again too often; stopping"
            exit 1
        fi
        echo "$(date) Arcanist asked to start again (exit code ${code})"
        sleep 1
        continue
    fi
    echo "$(date) Arcanist stopped (exit code ${code})"
    exit "${code}"
done
