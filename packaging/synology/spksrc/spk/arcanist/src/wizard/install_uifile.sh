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
      "type": "textfield",
      "desc": "The port Arcanist answers on (from 1024 to 65535). You can change it later in Arcanist's window in DSM.",
      "subitems": [{
          "key": "wizard_port",
          "desc": "Port",
          "defaultValue": "8484",
          "validator": {
            "allowBlank": false,
            "regex": {
              "expr": "/^(102[4-9]|10[3-9][0-9]|1[1-9][0-9]{2}|[2-9][0-9]{3}|[1-5][0-9]{4}|6[0-4][0-9]{3}|65[0-4][0-9]{2}|655[0-2][0-9]|6553[0-5])$/",
              "errorText": "Enter a port from 1024 to 65535."
            }
          }
        }]
    }, {
      "desc": "Arcanist runs as the internal service user <b>sc-arcanist</b>, which gets access to this shared folder. To add other folders as libraries, give <b>sc-arcanist</b> read permission on them: Arcanist's window in DSM shows which ones it can read and how."
    }]
}, {
  "step_title": "Setup code",
  "items": [{
      "type": "textfield",
      "desc": "After installing, open Arcanist from the DSM main menu to create the administrator account and your libraries. Arcanist asks for this code to make sure the person setting it up is you. Write it down.",
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
