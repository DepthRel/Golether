# Golether: privacy

Golether has no accounts, no advertising and no telemetry: it does not collect statistics about you or about what you
watch. The film, the voices, the cameras, the chat and the drawings go directly between the computers of the
participants, encrypted, and no server of the project sees them.

This page lists everything Golether stores and every address it contacts.

## What stays on your computer

Everything below lies in the `data` folder of the application (see "Where the data is kept" in the
[user guide](UserGuide.md)) and never leaves the computer by itself:

- the device key: it proves to the others that this is the same device;
- the contacts: the name and the key fingerprint of every device whose code you compared, whether you trust it, its
  last network address, your notes and when it was first and last seen;
- the settings, the AmneziaWG tunnels with their keys, the downloaded components;
- the log of the application in `data/logs`, kept for 7 days. It records connections, errors and network addresses of
  the participants, never the content of a session.

Chat messages and drawings are stored nowhere: when the session is closed, they are gone. A diagnostic report is saved
only when you ask for it; it holds the log and the settings, so look at it before you send it to anyone.

## What the other participants receive

The participants of your session see your name, your avatar, your network address (a direct connection needs it) and
the fingerprint of your device key; with the camera, the microphone or the chat switched on they get those too. The
host serves the film to the participants, who may pass pieces of it on to each other. The verification code is
compared by voice, not sent anywhere.

## Where Golether connects

| What | When | What the other side learns |
|---|---|---|
| The rendezvous server of the project (its address comes from `config.json` of the project page) | when a host opens a session through the server, and when a participant joins such a session | the network addresses of the host and of the participant, the fingerprint of the host's key, the random tag of the room; never the content of the session |
| The project page `depthrel.github.io` (GitHub Pages) | `updates.json` at start (the newest version; the check can be switched off in the settings); `config.json` when a host opens a session with the rendezvous server chosen automatically | your IP address, as for any web page; GitHub's privacy statement applies |
| GitHub and `gstreamer.freedesktop.org` | on Windows, only when you install a missing component with the **Install** button | your IP address |
| The public STUN servers of Google (`stun.l.google.com`) and Cloudflare (`stun.cloudflare.com`) | only when you set up an AmneziaWG tunnel: they tell the computer its outside address | your IP address and port; their own privacy policies apply |
| Your router (UPnP, NAT-PMP) | when the host asks the router to open a port | nothing beyond your local network |

A session can be started without the rendezvous server: choose the direct connection or your own server in the
invitation window ("Rendezvous server" in the [user guide](UserGuide.md)).

## The project page

The project page `depthrel.github.io/Golether` counts its visits with Umami Cloud. Umami uses no cookies and, by its
own description, keeps no IP addresses: it records the address of the page (without the part after `#` and `?`), the
page you came from, the browser, the system, the type of device, the size of the screen, the language and the country,
which it derives from your address. A press on a download button is counted too: which package (Windows, .deb,
archive, macOS) and from which place of the page. A browser that asks not to be tracked (Do Not Track) is not counted.
The invitation page (`/join/`) has no counter at all, and the application itself sends nothing to Umami.

## The public rendezvous server

The rendezvous server of the project only introduces the two sides to each other; after that they talk directly and
the server is no longer involved. It keeps the list of open rooms in memory while they are open. Its journal of events
(connections, rooms, failures, with network addresses) also lives only in memory, holds a limited number of the latest
events and is visible only to the administrator on the server machine itself. The system log of the server records
when a host holds or leaves a room, with the short fingerprint of its key and its network address; these lines stay as
long as the system of the server keeps its log. Nothing is sold or passed on to anyone.

Anybody can run their own rendezvous server: [Rendezvous server](RendezvousServer.md).

## Questions

Questions about your data, problems and ideas go to the maintainer: project.golether@gmail.com. The **Report a
problem** button (in the window about the application and in the window of an error) opens a letter in your mail
program with the version of Golether, the system and the language already filled in. Nothing is sent until you send
the letter yourself; it reaches the maintainer through Gmail.
