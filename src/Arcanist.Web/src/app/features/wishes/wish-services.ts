import type { WishLinkService } from '../../graphql/generated/graphql';

/** The services' own names (brands are not translated); `null` for an unknown page. */
const names: Record<WishLinkService, string | null> = {
  SPOTIFY: 'Spotify',
  YOU_TUBE_MUSIC: 'YouTube Music',
  YOU_TUBE: 'YouTube',
  DEEZER: 'Deezer',
  APPLE_MUSIC: 'Apple Music',
  MUSIC_BRAINZ: 'MusicBrainz',
  TIDAL: 'TIDAL',
  SOUND_CLOUD: 'SoundCloud',
  BANDCAMP: 'Bandcamp',
  AMAZON_MUSIC: 'Amazon Music',
  OTHER: null,
};

export function wishServiceName(service: WishLinkService | null | undefined): string | null {
  return service ? names[service] : null;
}

/** The recording's page in MusicBrainz. */
export function musicBrainzRecordingUrl(id: string): string {
  return `https://musicbrainz.org/recording/${id}`;
}
