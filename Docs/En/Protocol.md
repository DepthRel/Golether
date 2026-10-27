# Golether network protocol (version 1)

## Transport

TCP, the host's port is **47800** by default. Every connection:

1. TLS 1.3 (TLS 1.2 on Windows 10) with certificates on both sides. Chain and name validation is off: the
   participant checks the `PeerId` of the host's certificate against the invitation, the host computes the `PeerId`
   of the participant.
2. The client's preamble, 6 bytes: `47 4C 54 48` (`GLTH`), version `01`, purpose (`01` Control, `02` MediaData,
   `03` Relay, `04` RelayShared).
3. The host's answer, 1 byte: `01` means accepted.

`PeerId` = lowercase hex SHA-256 of the DER SubjectPublicKeyInfo of the device key (ECDSA P-256).

## Control

Frames: 4 bytes of length (big-endian) and UTF-8 JSON, at most 256 KiB. The type is given by the `$t` field; field
names are in camelCase. H is the host, P a participant.

| `$t` | Direction | Fields | Meaning |
|---|---|---|---|
| `hello` | P → H | `version`, `displayName`, `inviteToken`, `gallery` | the first message |
| `pending` | H → P | — | waiting for the host's decision |
| `welcome` | H → P | `sessionName`, `participants[]`, `playback`, `media`, `mediaDataStreams`, `relay`, `relayOverSession`, `relayShared`, `reconnectTicket` | the participant is admitted; `relay` is the port and the credentials of the host's TURN, created for one session; `relayOverSession` means the host accepts TURN over `Relay` streams and no separate port is needed; `relayShared` means it also accepts all of them in one `RelayShared` stream; `reconnectTicket` is the ticket for coming back to this session without a new invitation |
| `rejected` | H → P | `reason`, `message` | refusal, the connection is closed |
| `ping` | P → H | `clientSendTime` | a clock probe (t0, µs) |
| `pong` | H → P | `clientSendTime`, `hostReceiveTime`, `hostSendTime` | t0, t1, t2 |
| `request` | P → H | `request: {kind, position}` | an intention: `0` play, `1` pause, `2` seek, `3` play without waiting for those not ready |
| `state` | H → all | `state: PlaybackState` | the authoritative state |
| `status` | P → H | `status: ParticipantStatus` | once a second; the host replaces `peerId` with the verified one |
| `participants` | H → all | `participants[]`, `statuses[]` | once a second and on changes |
| `media` | H → all | `media: MediaDescriptor` | a new file |
| `rtc` | any | `peer`, `kind`, `payload` | WebRTC signalling; the host forwards it, putting in the verified sender |
| `moderate` | H → P | `microphone`, `camera` | the host switches a participant's devices off (switching off only) |
| `chat` | P → H → all | `id`, `kind` (`0` text, `1` reaction), `text`, `sender` | a chat message or a reaction; the host fills `sender` with the verified identifier, the value that was sent is discarded |
| `draw` | P → H → all | `strokeId`, `phase` (`0` begin, `1` continue, `2` end), `points[]` (`x`, `y` as fractions of the picture), `sender` | a pen stroke over the video; the host fills `sender` with the verified identifier |
| `avatar` | P → H → all | `image` (PNG, base64; empty resets), `sender` | a participant's picture, shown while the camera is off; the host fills `sender`; a new participant gets the avatars already chosen from the host |
| `gallery` | H → P | `catalogId`, `title`, `total`, `offset`, `items[]` | a page of a shown folder; an empty `catalogId` ends the showing |
| `show` | P → H → all | `catalogId`, `index`, `version`, `sender` | the item on everybody's screen; the host numbers the decision and fills `sender` |
| `thumb` | P → H → P | `catalogId`, `index`, `image` (JPEG, base64) | a request for a small picture (without `image`) and the answer |
| `bye` | any | `reason` | leaving; `reason` is always empty and is ignored by the receiver |

`PlaybackState`: `state` (`0` paused, `1` playing), `position` (`hh:mm:ss.fffffff`), `referenceTime` (µs by the
host's clock), `rate`, `version`, `origin`, `cause` (`0` initial, `1` play, `2` pause, `3` seek, `4` waiting for
participants, `5` everybody is ready, `6` the film ended, `7` play without waiting for those not ready).

Everything that came over the network is checked before use:

- **A state** is accepted by a participant only when the mode is known, the position lies between zero and 30 days,
  and `rate` is a finite number from 0.5 to 2. Otherwise the state is skipped (in `welcome`, the entry is refused).
- **An intention** with an unknown `kind` is dropped by the host. The requested position is clamped to the duration
  of the file and, while that is unknown, to the same 30 days.
- **A participant's status** is bounded by the host before any computation or broadcast: times within ±30 days, ping
  from 0 to 600,000 ms, byte counters not below zero. A participant bounds the statuses from `participants` the same
  way.
- **The file name** in `media` is at most 255 characters, with no control characters and no path separators (`/`,
  `\`). The rule is the same on every system: the host replaces such characters with `_` before sending, and a name
  valid on its system (with a colon on Linux, for example) is accepted by the others. The name is only shown; it
  never serves as a path.

Reasons of a refusal: `0` invalid invitation, `1` declined, `2` the host did not answer, `3` the session is full,
`4` incompatible version, `5` removed by the host. A `rejected` message with reason `5` also arrives in the middle of
a session: that is how the host removes an admitted participant.

The `message` field of `rejected` is a short note for the log (the name of the reason in English); the participant
does not show it but words the reason in the language of their own interface from the `reason` field. The host sends
`bye` without `reason` for the same reason: texts for people do not travel over the network. A participant ignores
the field even when a host of another build fills it, and words the end of the session itself.

The order of a connection: `hello` → (`pending`) → `welcome` | `rejected`. The token of an invitation works until
its lifetime ends and serves everybody who was invited, but brings at most 16 join requests; trusted contacts may
connect without a token. A device the host removed or declined needs a token issued later: for an earlier one it gets
`rejected` with reason `5` or `1` at once, without the host being asked.

### Chat and reactions

- Text: at most 100 characters after cleaning. Control characters, surrogates and formatting characters (except
  U+200D) are cut out, no more than two line breaks in a row, surrounding whitespace is removed.
- A reaction: one of a fixed set (`👍 😂 😮 😢 ❤️ 🔥 👏`), otherwise the message is dropped.
- `id` is up to 32 hexadecimal digits, chosen by the sender; the host does not touch it.
- Rate: at most 5 texts and 15 reactions in 5 seconds per participant (a sliding window on the host).
- The host sends a message to everybody, the sender included: the order is the same for all. Nothing is stored on
  disk.

### The pen over the video

- A stroke is a sequence of `draw` messages with one `strokeId`: a `begin`, any number of `continue`s and an `end`.
  Points are collected by the sender and leave in batches about every 60 ms, at most 128 points in a message.
- `strokeId` is up to 32 hexadecimal digits, chosen by the sender; the host does not touch it.
- Coordinates are fractions of the picture (`0`–`1`), not window pixels: the stroke lands on the same place of the
  frame for everybody. Values beyond the bounds are clamped; `NaN` and infinities are thrown away.
- An empty list of points is allowed only for the `end` phase.
- Rate: at most 40 messages a second per participant (a sliding window on the host); the excess is dropped.
- A stroke belongs to whoever began it: a piece with the same `strokeId` from another participant is not accepted by
  the receiver.
- The receiver keeps a finished stroke for 0.3 seconds and fades it out over the next 1.2; an unfinished stroke (the
  author disconnected) disappears after 20 seconds. At most 24 strokes are drawn at once. Nothing is stored.

### Avatars

- A participant sends their avatar in an `avatar` message without the `sender` field; the host puts in the verified
  identifier of the sender and sends it to everybody, the sender included, so the order and the picture are the same
  for all.
- `image` is a PNG of at most 128 KiB (so it fits into the 256 KiB message limit together with base64) and at most
  1024 × 1024 pixels. The bytes are untrusted: the PNG signature, the file size and the picture size from the header
  (IHDR) are checked, otherwise the message is dropped; a small file can declare a picture worth gigabytes of memory.
  An empty field resets the avatar back to the coloured circle with a letter.
- Rate: at most 5 messages in 10 seconds per participant (a sliding window on the host); the excess is dropped.
- The host remembers the last avatar of every participant and sends them to a new participant right after
  `welcome`, so the newcomer sees the pictures already chosen. An avatar is forgotten when the participant leaves.
- An avatar is shown in place of the camera while the camera is off. The file of the chosen avatar is kept in
  `data/avatars`.

### Coming back to a session

`reconnectTicket` is 48 hexadecimal digits, issued on admission and alive while the session runs. The participant
sends it in the `inviteToken` field of `hello`; the host compares the ticket with the one issued for that device in
constant time and skips the invitation check. The host's confirmation is asked as for an ordinary entry. When the
host removes a participant, the ticket is taken away.

### A shown folder

- Only participants that announced `gallery: true` in `hello` receive `gallery`, `show` and `thumb`: an older
  participant would drop the connection on an unknown type.
- `catalogId` is 32 hexadecimal digits, random for every showing. An item is `fileName`, `length`, `key`, `kind`
  (`0` picture, `1` video) and `folder`: the nested folder relative to the shown one, names joined by `/`, at most
  128 characters, every name cleaned like a file name. `key` is 64 hexadecimal digits, random for every showing; its
  first 8 bytes are the file key of MediaData requests, so a picture is fetched over the media streams in checked
  pieces. A video is never fetched this way.
- At most 2000 items, 50 in a page; a picture is at most 64 MiB. A page whose numbers do not agree, or with an item
  that breaks a rule, is dropped as a whole.
- `show` from a participant is an intention (`version` and `sender` are ignored). The host accepts at most 8 a
  second from a participant, numbers its decision and sends it to everybody with the verified sender. A video put on
  the screen becomes the film: the host sends `media` and `state` first, then `show`. A picture put there pauses
  the film.
- A small picture is a JPEG of at most 320 × 320 pixels and 96 KiB, made once by the host. The host answers at most
  400 requests in 10 seconds per participant; anything that is not a JPEG within the limits arrives as an empty answer.
- A participant looking at a picture alone reports its index in `galleryView` of its status, bounded by the number of
  items; the others show where everybody looks.
- A newcomer receives the pages and the current `show` right after `welcome`.

## MediaData

A request, 17 bytes: the operation (`01` GetChunk for a piece, `02` GetHash for the hash only), the piece index
(int64 BE), the file key (uint64 BE, the first 8 bytes of the quick identifier). The answer to GetHash comes without
data.

An answer, 45 bytes and the data: the status (`0` OK, `1` outside the file, `2` another file, `3` read error), the
index (int64 BE), the length (int32 BE), the SHA-256 of the data (32 bytes).

The piece size is 256 KiB by default (64 KiB – 16 MiB are allowed). The quick identifier = SHA-256(length int64 LE,
the first MiB, the middle MiB, the last MiB).

## Relay

The stream carries TURN (RFC 5766 in TCP framing) unchanged: the same thing the client would send to the host's
relay port, with the same credentials from `welcome`. The host accepts such a stream only from an admitted
participant and closes it together with the admission. The allocation is created on the host's address that the
connection reached.

On the participant's side the streams are opened by `RelayTunnel`: it listens on a port of `127.0.0.1` that the
cameras' `turn-server` points at, and for every connection to it opens a `Relay` stream of its own by the route the
session took (a direct address or a meeting through the rendezvous server). If `welcome` has no `relayOverSession`
(a host of an earlier version), the participant goes to the relay port directly, as before.

### RelayShared

A `RelayShared` stream carries all TURN connections of a participant at once, so that the cameras and voices need
one connection through the routers (through a rendezvous server, one meeting) instead of a dozen. It is a sequence
of frames: the channel number (4 bytes, big-endian), the kind (1 byte), the payload length (2 bytes, big-endian),
the payload (at most 16384 bytes).

| Kind | Sent by | Meaning |
|---|---|---|
| `1` open | participant | starts a channel; no payload |
| `2` data | both | bytes of the channel: the same TURN in TCP framing |
| `3` close | both | the channel is over; no payload |

The relay serves each channel as a client of its own. At most 16 channels are open at once; an `open` beyond that
is answered with `close`. Data for a channel that does not exist is dropped; a frame of another kind or with a
longer payload ends the whole stream. The participant uses this stream when `welcome` has `relayShared` and opens a
new one when it broke; without the flag (a host of an earlier version) every connection gets a `Relay` stream of
its own. The price of sharing is that a lost packet holds back all channels until it is sent again.

## Exchanging pieces between participants

It runs over the WebRTC data channel `golether` (SCTP over DTLS; messages are available only after the peer's
certificate has been checked). All numbers are big-endian.

| Type | Content | Meaning |
|---|---|---|
| `1` have | file key (8), number of ranges (2), ranges: start (4), length (4) | which pieces are there; every 2 s |
| `2` request | file key (8), request number (4), piece index (4) | a request to send a piece |
| `3` data | request number (4), offset (4), full size (4), data | a part of a piece (16 KiB each, in order) |
| `4` miss | request number (4) | the piece is not there or the giver is busy (at most 2 uploads per participant) |

A received piece is accepted only when its SHA-256 matches the hash from the host (GetHash); otherwise the piece is
requested from the host. The same happens when the transfer stalls for 5 seconds or the piece has not arrived whole
within 30 seconds: a participant who sends a little at a time cannot hold the film up for the others.

## Invitation

`golether://join/v1/` + base64url(JSON), at most 4096 characters:

```json
{ "token": "<22 base64url characters, 128 bits>", "expires": 1789000000,
  "host": "<PeerId>", "name": "You", "session": "Movie night",
  "endpoints": ["192.168.1.10:47800", "[2001:db8::5]:47800"],
  "meet": "203.0.113.7:47900", "room": "<22 base64url characters, 128 bits>" }
```

`token` and `expires` (Unix seconds) are written first: the rest is the same in every invitation of a session, so a
new invitation differs from its first characters. The order of the fields means nothing to a reader. `endpoints`
holds from 1 to 16 addresses.

The same invitation as a web link: `https://depthrel.github.io/Golether/join/#` + the same base64url. The browser
does not send the part after `#` to the server; the page turns it into `golether://join/v1/…` and hands it to the
application. The application accepts both spellings.

The `meet` and `room` fields are optional and come only together: the host's rendezvous server and its room there
(a new one for every session). With only one of them, or with a malformed one, the invitation counts as damaged.
Old versions do not read these fields. The protocol of the server itself is described in
[RendezvousServer.md](RendezvousServer.md#protocol-version-1).

## Verification code

Both sides compute the code on their own; it never travels over the network.

```
hash   = SHA-256(UTF-8("golether-sas-v1|" + low + "|" + high + "|" + context))
words  = the 24 bits of hash[0..2] (big-endian), cut into three numbers of 6 bits (0–63), the highest first
number = uint16 little-endian of hash[3..4] mod 100
```

`low` and `high` are the two `PeerId`s in ordinal order, so the result does not depend on who computes it. The
context of an entry is the token the participant presents in `hello`: the invitation token, or the
`reconnectTicket` when coming back. The context of a tunnel is the `offerId` of its offer (see below).

A word number is turned into a word only on the screen, by the language pack (`Verification.Word.00` …
`Verification.Word.63`). Every language names the same 64 things in the same order, so the code is the same in every
language, and two people with different interface languages see translations of each other's words.

## AmneziaWG packages

`golether-awg:<offer|answer>:v1:<base64url(JSON body)>.<base64url(signature)>`.
The signature: ECDSA P-256/SHA-256 (IEEE P1363) over the ASCII string before the dot. Spaces and line breaks inside
a package are ignored, so a package may be forwarded with line wraps.

The body of an offer: `offerId`, `hostName`, `hostCertificate` (DER, base64), `hostPublicKey` (AWG),
`hostEndpoints[]`, `hostAddress`, `assignedAddress`, `s1`, `s2`, `headers[4]`, `ephemeralKey` (SPKI P-256, base64),
`expiresAt`.

The body of an answer: `offerId`, `participantName`, `participantCertificate`, `participantPublicKey`,
`participantEndpoints[]`, `ephemeralKey`.

PSK = base64(SHA-256(ECDH(ephemeral P-256 keys))): the shared secret of two single-use keys, passed through SHA-256.
A package with an incomplete body or with a key that is not on the P-256 curve counts as damaged.

Verification code = `VerificationCode(hostPeerId, participantPeerId, offerId)`.
