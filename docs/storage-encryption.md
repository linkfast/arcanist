# Encrypted S3 libraries — format 1

An S3 library can be encrypted on the Arcanist server with a key its owner keeps (Libraries → Library in S3 →
*Encrypt with a key of your own*, or `encryptionKey` in `createS3Library`). The S3 service then stores neither the
music nor its names in the clear: only how many objects there are, their sizes, their dates and the shape of the
folders. This document describes the format, so the data can be read with nothing but the key
(`src/Arcanist.Core/Storage/S3/S3Encryption.cs` is the implementation).

## The key

256 bits, written in base64 (or base64url). Three keys are derived from it with HKDF-SHA256 (no salt), 32 bytes each:

| Info | Used for |
|---|---|
| `arcanist-s3-names-mac-v1` | the synthetic IV of names |
| `arcanist-s3-names-cipher-v1` | encrypting names |
| `arcanist-s3-key-wrap-v1` | wrapping each file's key |

## Names

Every segment of a path (relative to the library's prefix) is encrypted on its own, so the same path always gives
the same object key and folders stay folders:

1. `iv` = the first 12 bytes of HMAC-SHA256(names-mac key, segment in UTF-8).
2. `ciphertext` = AES-256-CTR(names-cipher key) of the segment, the counter block being `iv` followed by a 32-bit
   big-endian block number starting at 0.
3. The segment is `base64url(iv ‖ ciphertext)`, without padding.

Decrypting recomputes the HMAC of the plaintext: a different `iv` means it is not a name of this key. An object key is
`prefix/segment₁/…/segmentₙ`. Objects whose names do not decrypt are not part of the library.

## Contents

Every object has its own 256-bit key (DEK) and an 8-byte nonce prefix. Its plaintext is cut in blocks of 65,536
bytes (the last one shorter; an empty file is one empty block), and each block *i* (from 0) is sealed with
AES-256-GCM:

- key: the DEK;
- nonce: the prefix followed by *i* as a 32-bit big-endian number;
- associated data: *i* as a 64-bit big-endian number, then one byte, `1` for the last block and `0` otherwise.

The object is the blocks one after another, each followed by its 16-byte tag, so its size is
`plaintext + 16 × blocks`, and a range of the plaintext reads only its blocks. Blocks cannot be dropped, reordered or
taken from another file without the tag check failing.

The object's metadata `x-amz-meta-arcanist-enc` is `1.<base64url(nonce prefix)>.<base64url(wrap)>`, where `wrap` is a
12-byte nonce, the DEK sealed with AES-256-GCM under the key-wrap key (associated data: `arcanist-dek-v1`), and its
16-byte tag. Changing the owner's key only rewrites this metadata (an S3 copy onto itself), never the audio. Every
object is stored as `application/octet-stream`.

## The key marker

The object `<prefix>/.arcanist-key` (not encrypted; its name does not decrypt, so it is never part of the library)
holds `1.` followed by the base64url of 16 bytes derived from the key with HKDF-SHA256 (no salt, info
`arcanist-s3-key-check-v1`). It says which key encrypted the prefix without revealing it. Arcanist writes it the first
time it uses a key there, and refuses another key (`s3_wrong_key` when creating the library; the source is
unavailable, and nothing is marked missing, when scanning), so one prefix never mixes two keys.

## What it protects

The data at rest, and in the hands of whoever stores it: the S3 service, and anyone reading the bucket. The Arcanist
server itself reads the music to play it, so the key is in its memory while it runs. Whoever operates the server can
reach it then; whoever operates only the storage cannot.
