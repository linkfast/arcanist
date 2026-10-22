#!/bin/sh
# Creates Arcanist's configuration in openmediavault's database, once:
#   <services><arcanist><enable>1</enable><port>8484</port><serveraudio>0</serveraudio><shares/></arcanist></services>
set -e

. /usr/share/openmediavault/scripts/helper-functions

SERVICE_XPATH_NAME="arcanist"
SERVICE_XPATH="/config/services/${SERVICE_XPATH_NAME}"

if ! omv_config_exists "${SERVICE_XPATH}"; then
    omv_config_add_node "/config/services" "${SERVICE_XPATH_NAME}"
    omv_config_add_key "${SERVICE_XPATH}" "enable" "1"
    omv_config_add_key "${SERVICE_XPATH}" "port" "8484"
    omv_config_add_key "${SERVICE_XPATH}" "serveraudio" "0"
    omv_config_add_node "${SERVICE_XPATH}" "shares"
fi

exit 0
