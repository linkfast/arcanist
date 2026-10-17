# Arcanist service setup.
# The server is a .NET build in share/arcanist, run by the runtime of the dotnet10-runtime package. All its state
# (settings, SQLite database, keys, caches, logs) goes to the package's var folder, which DSM keeps on upgrades.
# bin/arcanist-run supervises it: it reads the port from var/package.conf (set by the install wizard and the
# package's window in DSM) and starts the server again when it asks to (factory reset, server restore, a new port).
# FFmpeg comes from the ffmpeg8 package, which Arcanist finds by itself.
DOTNET_ROOT="/var/packages/dotnet10-runtime/target/share/dotnet"
SERVICE_COMMAND="env HOME=${SYNOPKG_PKGVAR} ARCANIST_DATA_DIR=${SYNOPKG_PKGVAR} ARCANIST_HOME=${SYNOPKG_PKGDEST}/share/arcanist DOTNET_ROOT=${DOTNET_ROOT} DOTNET_CLI_TELEMETRY_OPTOUT=1 ${SYNOPKG_PKGDEST}/bin/arcanist-run"

SVC_BACKGROUND=y
SVC_WRITE_PID=y
SVC_WAIT_TIMEOUT=60

service_postinst ()
{
    # The setup code shown by the install wizard. Arcanist asks for it when its setup wizard is opened from
    # another device, which on a NAS is always the case.
    if [ "${SYNOPKG_PKG_STATUS}" = "INSTALL" ] && [ -n "${wizard_setup_code}" ]; then
        echo "${wizard_setup_code}" > "${SYNOPKG_PKGVAR}/setup-code.txt"
    fi

    # The port chosen in the install wizard; the package's window changes it later.
    if [ "${SYNOPKG_PKG_STATUS}" = "INSTALL" ]; then
        port="$(echo "${wizard_port}" | tr -cd '0-9')"
        echo "PORT=${port:-${SERVICE_PORT}}" > "${SYNOPKG_PKGVAR}/package.conf"
    elif [ ! -f "${SYNOPKG_PKGVAR}/package.conf" ]; then
        echo "PORT=${SERVICE_PORT}" > "${SYNOPKG_PKGVAR}/package.conf"
    fi
}
