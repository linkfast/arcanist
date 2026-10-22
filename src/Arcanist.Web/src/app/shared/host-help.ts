import { Component, OnInit, computed, inject, input } from '@angular/core';
import { I18n } from '../core/i18n/i18n';
import { Host } from '../core/server/host';

export type HostHelpTopic = 'folders' | 'audio' | 'restart' | 'dlna';

/**
 * The steps for something that needs doing outside Arcanist, written for the host it runs on: the text of
 * `host.<topic>.<kind>` (Synology, Docker, a Windows service…), or `host.<topic>.default` when that kind has none.
 * Nothing when neither exists (DLNA only needs steps in containers). `{user}` and `{data}` are the host's.
 */
@Component({
  selector: 'app-host-help',
  host: { class: 'block' },
  template: `
    @if (text(); as text) {
      <p class="whitespace-pre-line text-xs text-muted-foreground">{{ text }}</p>
    }
  `,
})
export class HostHelp implements OnInit {
  private readonly i18n = inject(I18n);
  private readonly host = inject(Host);

  readonly topic = input.required<HostHelpTopic>();

  protected readonly text = computed(() => {
    const host = this.host.current();
    const kind = host?.kind ?? 'OTHER';
    const key = [`host.${this.topic()}.${kind}`, `host.${this.topic()}.default`].find((k) =>
      this.i18n.has(k),
    );
    const text = key
      ? this.i18n.t(key, { user: host?.user ?? '?', data: host?.dataDirectory ?? '?' })
      : null;
    // Without a supervisor, a restart is a stop: say it first.
    return this.topic() === 'restart' && host && !host.supervised
      ? `${this.i18n.t('host.restart.unsupervised')}
${text ?? ''}`.trim()
      : text;
  });

  ngOnInit(): void {
    void this.host.load();
  }
}
