# Arcanist service setup.
# The server is a self-contained .NET build in share/arcanist; all its state (settings, SQLite database, keys,
# caches, logs) goes to the package's var folder, which DSM keeps on upgrades.
ARCANIST="${SYNOPKG_PKGDEST}/share/arcanist/arcanist"
SERVICE_COMMAND="env HOME=${SYNOPKG_PKGVAR} ARCANIST_DATA_DIR=${SYNOPKG_PKGVAR} ${ARCANIST} --urls http://*:${SERVICE_PORT}"

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
}
