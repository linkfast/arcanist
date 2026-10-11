SPK_NAME = arcanist
SPK_VERS = 0.1.0
SPK_REV = 1
SPK_ICON = src/arcanist.png

DEPENDS = cross/arcanist

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
SERVICE_PORT = 8484
SERVICE_PORT_TITLE = Arcanist (HTTP)
ADMIN_PORT = $(SERVICE_PORT)

WIZARDS_DIR = src/wizard/
# The music share is created if missing and made accessible to sc-arcanist.
SERVICE_WIZARD_SHARENAME = wizard_music_folder

# /tmp may be small or not accessible on DSM.
USE_ALTERNATE_TMPDIR = 1

include ../../mk/spksrc.spk.mk
