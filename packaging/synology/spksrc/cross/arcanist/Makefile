PKG_NAME = arcanist
PKG_VERS = 0.1.0
PKG_EXT = tar.gz
PKG_DIST_NAME = $(PKG_NAME)-$(PKG_VERS)-linux-$(PKG_DIST_ARCH).$(PKG_EXT)
PKG_DIST_SITE = https://github.com/linkfast/arcanist/releases/download/v$(PKG_VERS)
PKG_DIR = $(PKG_NAME)-$(PKG_VERS)-linux-$(PKG_DIST_ARCH)

DEPENDS =

HOMEPAGE = https://github.com/linkfast/arcanist
COMMENT  = Self-hosted music server for the whole family.
LICENSE  = AGPL-3.0-or-later

INSTALL_TARGET = arcanist_install

# The release tarballs are self-contained .NET builds with the admin panel; use digests with multiple files.
PKG_DIST_ARCH_LIST = x64 arm64

include ../../mk/spksrc.common.mk
ifeq ($(findstring $(ARCH),$(x64_ARCHS)),$(ARCH))
PKG_DIST_ARCH = x64
else ifeq ($(findstring $(ARCH),$(ARMv8_ARCHS)),$(ARCH))
PKG_DIST_ARCH = arm64
endif

include ../../mk/spksrc.cross-install.mk

.PHONY: arcanist_install
arcanist_install:
	mkdir -p $(STAGING_INSTALL_PREFIX)/share/arcanist
	tar -cf - -C $(WORK_DIR)/$(PKG_DIR) . | tar -xf - -C $(STAGING_INSTALL_PREFIX)/share/arcanist
