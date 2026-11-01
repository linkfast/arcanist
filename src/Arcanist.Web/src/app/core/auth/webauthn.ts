import { ApiError } from '../api/rest';

/**
 * Whether passkeys can work at this address. Browsers ask for HTTPS (or localhost) and a domain name: a passkey cannot
 * belong to an IP address.
 */
export function passkeysUsable(): boolean {
  const host = window.location.hostname;
  const isIp = /^\d+\.\d+\.\d+\.\d+$/.test(host) || host.includes(':') || host.startsWith('[');
  return window.isSecureContext && !!window.PublicKeyCredential && !isIp;
}

function toBytes(base64Url: string): ArrayBuffer {
  const text = atob(base64Url.replace(/-/g, '+').replace(/_/g, '/'));
  const bytes = new Uint8Array(text.length);
  for (let i = 0; i < text.length; i++) bytes[i] = text.charCodeAt(i);
  return bytes.buffer;
}

function toText(buffer: ArrayBuffer | null): string | null {
  if (!buffer) return null;
  let text = '';
  for (const byte of new Uint8Array(buffer)) text += String.fromCharCode(byte);
  return btoa(text).replace(/\+/g, '-').replace(/\//g, '_').replace(/=+$/, '');
}

interface Descriptor {
  id: string;
  type: PublicKeyCredentialType;
  transports?: AuthenticatorTransport[];
}

const descriptors = (list: Descriptor[] | undefined) =>
  list?.map((d) => ({ ...d, id: toBytes(d.id) }));

/** What the browser's dialog does when the person cancels or the key says no: the same answer as a wrong key. */
async function ask<T>(request: () => Promise<T>): Promise<T> {
  try {
    return await request();
  } catch {
    throw new ApiError('passkey_failed');
  }
}

/** Creates a passkey from the server's options; the answer goes back to it as JSON. */
export async function createPasskey(optionsJson: string): Promise<string> {
  const options = JSON.parse(optionsJson);
  const credential = (await ask(() =>
    navigator.credentials.create({
      publicKey: {
        ...options,
        challenge: toBytes(options.challenge),
        user: { ...options.user, id: toBytes(options.user.id) },
        excludeCredentials: descriptors(options.excludeCredentials),
      },
    }),
  )) as PublicKeyCredential;
  const response = credential.response as AuthenticatorAttestationResponse;
  return JSON.stringify({
    id: credential.id,
    rawId: toText(credential.rawId),
    type: credential.type,
    clientExtensionResults: credential.getClientExtensionResults(),
    response: {
      clientDataJSON: toText(response.clientDataJSON),
      attestationObject: toText(response.attestationObject),
      transports: response.getTransports?.() ?? [],
    },
  });
}

/** Asks a passkey to answer the server's options; the answer goes back to it as JSON. */
export async function getPasskey(optionsJson: string): Promise<string> {
  const options = JSON.parse(optionsJson);
  const credential = (await ask(() =>
    navigator.credentials.get({
      publicKey: {
        ...options,
        challenge: toBytes(options.challenge),
        allowCredentials: descriptors(options.allowCredentials),
      },
    }),
  )) as PublicKeyCredential;
  const response = credential.response as AuthenticatorAssertionResponse;
  return JSON.stringify({
    id: credential.id,
    rawId: toText(credential.rawId),
    type: credential.type,
    clientExtensionResults: credential.getClientExtensionResults(),
    response: {
      authenticatorData: toText(response.authenticatorData),
      clientDataJSON: toText(response.clientDataJSON),
      signature: toText(response.signature),
      userHandle: toText(response.userHandle),
    },
  });
}
