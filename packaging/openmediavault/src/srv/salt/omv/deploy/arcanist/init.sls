# Arcanist's plugin for openmediavault: applies its settings to the arcanist service (a systemd drop-in).
# Copyright (C) 2026 LinkFast S.A. and Arcanist contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

# Make sure custom Jinja filters are registered.
{% set _ = salt['omv_utils.register_jinja_filters']() %}

include:
  - .{{ salt['pillar.get']('deploy_arcanist', 'default') }}
