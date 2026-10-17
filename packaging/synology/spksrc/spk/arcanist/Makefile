SPK_NAME = arcanist
SPK_VERS = 0.1.0
SPK_REV = 1
SPK_ICON = src/arcanist.png

DEPENDS = cross/arcanist
# SynoCommunity's .NET runtime runs the server; its FFmpeg converts audio (Arcanist finds it by itself).
SPK_DEPENDS = "dotnet10-runtime:ffmpeg8"

# .NET 10 runtime floor. The release tarballs exist for x64 and ARMv8 only.
MIN_GLIBC_VERSION = 2.27
REQUIRED_MIN_DSM = 7.2
UNSUPPORTED_ARCHS = $(32bit_ARCHS)

MAINTAINER = linkfast
MAINTAINER_URL = https://github.com/linkfast/arcanist
DESCRIPTION = Self-hosted music server: your own Spotify for the whole family. Create libraries from your shared folders and listen from any browser or device.
DESCRIPTION_SPN = Servidor de música autoalojado: tu propio Spotify para toda la familia. Crea bibliotecas a partir de tus carpetas compartidas y escúchalas desde cualquier navegador o dispositivo.
DISPLAY_NAME = Arcanist
CHANGELOG = "Initial release."
HOMEPAGE = https://github.com/linkfast/arcanist
SUPPORTURL = https://github.com/linkfast/arcanist/issues
LICENSE = AGPL-3.0-or-later
LICENSE_FILE = $(wildcard $(WORK_DIR)/arcanist-*/LICENSE)

STARTABLE = yes
SERVICE_USER = auto
SERVICE_SETUP = src/service-setup.sh
# The default port, opened in DSM's firewall; the install wizard and the package's window can choose another
# (var/package.conf, read by bin/arcanist-run).
SERVICE_PORT = 8484
SERVICE_PORT_TITLE = Arcanist (HTTP)

# Arcanist's window in DSM (status, port, shared folders it can read), for administrators.
DSM_UI_DIR = app
DSM_UI_CONFIG = src/app/config
DSM_APP_NAME = SYNOCOMMUNITY.Arcanist.AppInstance

WIZARDS_DIR = src/wizard/
# The music share is created if missing and made accessible to sc-arcanist.
SERVICE_WIZARD_SHARENAME = wizard_music_folder

# /tmp may be small or not accessible on DSM.
USE_ALTERNATE_TMPDIR = 1

POST_STRIP_TARGET = arcanist_extra_install

include ../../mk/spksrc.spk.mk

.PHONY: arcanist_extra_install
arcanist_extra_install:
	install -m 755 -d $(STAGING_DIR)/bin $(STAGING_DIR)/app
	install -m 755 src/arcanist-run.sh $(STAGING_DIR)/bin/arcanist-run
	install -m 644 src/app/arcanist.js $(STAGING_DIR)/app/arcanist.js
	install -m 644 src/app/index.html $(STAGING_DIR)/app/index.html
	install -m 755 src/app/api.cgi $(STAGING_DIR)/app/api.cgi
