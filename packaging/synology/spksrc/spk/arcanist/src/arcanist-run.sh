#!/bin/sh
# Runs Arcanist on DSM and starts it again when it asks to be: after a factory reset or a server restore (it exits
# with code 75) and when the package's window changes the port (it leaves "restart-requested" and stops the server).
# Reads the port from package.conf on every start. DSM stops the package with TERM, which is passed on to Arcanist.
#
# Environment: ARCANIST_DATA_DIR (the package's var folder), ARCANIST_HOME (the server's folder) and DOTNET_ROOT (the
# .NET runtime that runs it; without it, the server's own launcher, for a self-contained build).

conf="${ARCANIST_DATA_DIR}/package.conf"
flag="${ARCANIST_DATA_DIR}/restart-requested"
child=""
stopping=""
# Starts asked for in the last minute: a server that keeps asking is stopped instead of looping.
recent=""

trap 'stopping=1; [ -n "${child}" ] && kill -TERM "${child}" 2>/dev/null' TERM INT

port_from_conf ()
{
    port="$(sed -n 's/^PORT=\([0-9][0-9]*\)$/\1/p' "${conf}" 2>/dev/null | tail -n 1)"
    if [ -z "${port}" ] || [ "${port}" -lt 1024 ] || [ "${port}" -gt 65535 ]; then
        port=8484
    fi
    echo "${port}"
}

while :; do
    rm -f "${flag}"
    port="$(port_from_conf)"
    echo "$(date) Starting Arcanist on port ${port}"
    if [ -n "${DOTNET_ROOT:-}" ]; then
        "${DOTNET_ROOT}/dotnet" "${ARCANIST_HOME}/arcanist.dll" --urls "http://*:${port}" &
    else
        "${ARCANIST_HOME}/arcanist" --urls "http://*:${port}" &
    fi
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
    if [ "${code}" -eq 75 ] || [ -f "${flag}" ]; then
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
