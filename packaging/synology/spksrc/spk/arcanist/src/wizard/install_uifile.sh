#!/bin/sh
# Install wizard. Generated so it can show a fresh setup code: the same alphabet and format as the server's
# (Setup/SetupCode.cs), without characters that are easy to confuse.

code="$(tr -dc 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789' < /dev/urandom | head -c 8)"
code="$(echo "${code}" | cut -c1-4)-$(echo "${code}" | cut -c5-8)"

cat <<EOF_WIZARD > "${SYNOPKG_TEMP_LOGFILE}"
[{
  "step_title": "Music folder",
  "items": [{
      "type": "textfield",
      "desc": "Shared folder with your music. Enter the name of the shared folder only, without a path. It is created if it does not exist.",
      "subitems": [{
          "key": "wizard_music_folder",
          "desc": "Shared music folder",
          "defaultValue": "music",
          "validator": {
            "allowBlank": false,
            "regex": {
              "expr": "/^[^<>: */?\"]*$/",
              "errorText": "Enter a shared folder name only, without path separators or special characters."
            }
          }
        }]
    }, {
      "desc": "Arcanist runs as the internal service user <b>sc-arcanist</b>, which gets access to this shared folder. To add other folders as libraries, give <b>sc-arcanist</b> read permission on them. See <a target=\"_blank\" href=\"https://docs.synocommunity.com/user-guide/permissions/\">Permission Management</a>."
    }]
}, {
  "step_title": "Setup code",
  "items": [{
      "type": "textfield",
      "desc": "After installing, open Arcanist (port 8484) to create the administrator account and your libraries. Arcanist asks for this code to make sure the person setting it up is you. Write it down.",
      "subitems": [{
          "key": "wizard_setup_code",
          "desc": "Setup code",
          "defaultValue": "${code}",
          "validator": { "allowBlank": false }
        }]
    }, {
      "desc": "In Arcanist's setup, add your music as a library: the shared folder is under <b>/volume1</b> (or the volume where it was created)."
    }]
}]
EOF_WIZARD
exit 0
