# Copyright (C) 2026 LinkFast S.A. and Arcanist contributors
# SPDX-License-Identifier: AGPL-3.0-or-later

{% set config = salt['omv_conf.get']('conf.service.arcanist') %}
{% set dropin = '/etc/systemd/system/arcanist.service.d/openmediavault.conf' %}

configure_arcanist_dropin:
  file.managed:
    - name: "{{ dropin }}"
    - source:
      - salt://{{ tpldir }}/files/openmediavault.conf.j2
    - template: jinja
    - context:
        config: {{ config | json }}
    - makedirs: True
    - user: root
    - group: root
    - mode: '0644'

arcanist_systemctl_daemon_reload:
  cmd.run:
    - name: systemctl daemon-reload
    - onchanges:
      - file: configure_arcanist_dropin

{% if config.enable | to_bool %}

start_arcanist_service:
  service.running:
    - name: arcanist
    - enable: True
    - require:
      - cmd: arcanist_systemctl_daemon_reload
    - watch:
      - file: configure_arcanist_dropin

{% else %}

stop_arcanist_service:
  service.dead:
    - name: arcanist
    - enable: False

{% endif %}
