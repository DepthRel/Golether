# Golether: user guide

## What you need

- Windows 10 or newer, Linux, or macOS 14+.
- The Windows package already contains the components for video and cameras. If one is missing, press **Install**
  in the "Components" card of the start screen: the application downloads it from the official site, checks the
  checksum and installs it. No administrator rights are needed. On Linux and macOS the card shows a ready command for
  the terminal: copy it with the **Copy** button.
- A video can be opened right away: "Open with → Golether" creates a session and shows that file.
- Tunnels need AmneziaWG. On Windows there is nothing to install separately: the first time you open the tunnel
  window, Golether downloads its own engine (about 4 MB). On Linux and macOS you need `amneziawg-tools` together with
  the `amneziawg` module / `amneziawg-go`. Golether itself never has to run as administrator: the system asks for
  permission when a tunnel is brought up.

## Interface language

Golether speaks English, German, French, Spanish, Portuguese and Russian. On the first start the language follows the
operating system: a system in one of these languages gets it, whatever the region (Swiss German, Canadian French,
Brazilian Portuguese and so on), and any other language gets English. The choice is remembered, and later starts use
it instead of the system language.

To change the language, open **Settings** (the button at the top right), section **General settings**: the list
shows the flag and the name of every language. After **Apply** every text changes at once, no restart is needed, and
the choice is stored. Error messages use the same language. The reasons of a refusal sent by the host are shown in
your language even when the host uses another one.

The words of the verification code are shown in your language too. Every language names the same 64 things, so when
the host uses another language, their words are translations of yours: *owl · lighthouse · wind · 47* on one screen is
*Eule · Leuchtturm · Wind · 47* on the other. Compare the meaning of the words and the number.

## Show a film (you are the host)

1. Enter your name and the session name and press **Create a session**. The port of your computer (47800 by
   default) and the request to the router are under **Advanced**: change them only when the port is busy or was
   forwarded by hand.
2. Press **Show** → **File** and choose a video. The file stays with you: participants receive it piece by piece while
   watching and keep it in memory only.
3. Press **Invite**, copy the **link** and send it to your guests any way you like. A click on it opens Golether on
   the guest's computer and asks whether to join. The link works for 15 minutes. If the link does not open the
   application for a guest, send the **key** from the same window: it is pasted into Golether by hand and works for
   15 minutes or an hour.
   One invitation is good for the whole company: post the link in your group chat, and everyone can join with it
   until it expires. You still let each guest in yourself. The lifetime limits only the moment of joining: a guest who
   got in stays in the session after the invitation has expired.
   With "Open the port on the router automatically" switched on, Golether asks the router to open the port (UPnP or
   NAT-PMP), adds the public address to the invitation and reports the result in the event feed. If the router cannot
   do it or the address of your provider is not public, add a public address with a port forwarded by hand, or the
   address inside an AWG tunnel (invitation window, **Advanced**).
   The easiest way is the rendezvous server. The **Connection method** switch of the start screen is set to
   **Automatically** by default: Golether finds the server of the project by itself, it goes into the invitation, and
   guests from other networks connect without open ports on either side; see [below](#rendezvous-server).
4. When a participant connects, a window with the verification code appears. Read the code aloud. If the participant
   sees the same code, press **The codes match, let them in**.
5. Play, pause and seeking act on every participant.
6. Cameras and voice start by themselves when you enter a session: each participant's tile on the right shows their
   camera, and the voices of the others are mixed into one stream. The camera and the microphone are chosen in
   **Settings**, section **Camera and microphone** (the system defaults are used otherwise). A camera or microphone
   that is switched off is really released.

## Join (you are a participant)

1. Click the invitation link: the browser offers to open Golether, and Golether asks whether to join the session
   (showing its name and the name of the host). If you are in another session already, it asks whether to switch.
   Instead of clicking, you can paste the key or the link itself into the **Invitation** field and press **Connect**.
2. A verification code appears. Compare it with the host by voice. If the codes differ, press **End**: the
   connection may have been tampered with.
3. If you already have the same file, press **My copy of the file** and choose it: playback will run from your disk.

## Controls

| Action | Button | Key |
|---|---|---|
| Play / pause for everybody | ▶ / ❚❚ | Space |
| Back / forward 10 s | ◀◀ / ▶▶ | ← / → |
| Seek | the slider (applies when you release it) | — |
| Full screen | ⛶ | F11, Esc |
| Play / pause by click | a click on the video | — |
| Full screen by click | a double click on the video area (works before a film is chosen, too) | — |
| Your own volume | the speaker icon on the right: hover for a 0–100 % scale, click to mute and unmute, wheel to change | — |
| Microphone and camera | "Microphone on" / "Camera on" under the participant tiles | — |
| Sound and subtitles | the "CC" icon in the player bar: everyone has their own tracks | — |
| Reaction for everybody | the smiley icon in the player bar | — |
| Draw over the video | the pen icon in the player bar | — |
| Chat and events | the **CHAT** and **EVENTS** tabs at the bottom right; a click on the open tab or on the arrow hides the panel | — |
| Send a message | the field at the bottom of the chat panel | Enter |
| Volume of another participant | right click on their tile → slider | — |
| A participant's camera in its own window | right click on their tile → **Open in a separate window** | — |

In a narrow window the "Settings", "Invite" and "End" buttons fold into the **⋯** button at the top right.

Volume and mute act on your side only: the others notice nothing. A scale at 0 mutes the sound, and another click on
the speaker brings the previous volume back.

### Full screen

The cursor over the video disappears after three seconds of rest and comes back at the first movement, in any window
mode. Over the participant tiles and the chat it is always visible.

In full screen the status bar at the bottom is hidden and the video takes the whole height. The top bar and the
player bar lie over the video and slide away three seconds after the cursor leaves them; the picture does not change
its size. Move the cursor to the top or bottom edge of the screen and the bars slide back and stay while the cursor is
on them. Nothing hides while a menu of a bar is open, the slider is being dragged or the pen is on.

If you want the bars all the time, switch on **Always show the timeline** in **Settings**, section **Display**: in
full screen both bars stay in place and the video is fitted between them.

### Width of the participant panel and order of the tiles

In **Settings**, section **Display**, switch on **Adjustable layout**: a thin border appears between the video and the
participants. Drag it to make the participant panel wider; the camera tiles grow with it. A double click on the border
restores the original width.

With three participants or fewer the tiles form one column. With more participants and a panel stretched to over
three times its original width, the tiles form two columns, so more people fit on the screen.

With the same setting the tiles can be swapped: drag one onto another. The order is visible to you only and lasts
until the application is closed; new participants go to the end. Switching the setting off restores the original width
and order and removes the border.

### A participant's camera in a separate window

Right click on a participant's tile → **Open in a separate window**. The window shows only that camera; you can
stretch it or move it to another screen. Closing the window affects nothing; when the participant leaves, it closes
by itself.

### Your own audio tracks and subtitles

The **CC** icon in the player bar opens the list of the film's audio tracks and subtitles. The choice acts on your
side only: one watches the original, another the dub, a third reads subtitles. The chosen language is remembered and
applied to the next film that has such a track. **Subtitles from a file…** adds an `.srt` or `.ass` file from your
computer, **Sound from a file…** a separate audio track (your own dubbing, for example). Only you see and hear
either.

### Chat and reactions

There are two tabs at the bottom right: **CHAT** and **EVENTS**. A click on the open tab (or on the arrow on the
right) hides the panel entirely and gives the room to the participant tiles. While the chat is hidden, its tab shows
a counter of new messages. Messages travel over the same protected channel as the rest of the session and are stored
nowhere: when the session is closed, they are gone.

While the chat is out of sight, an orange "i" in a ring also sits at the corner of its tab, and while the whole
panel is hidden, a short orange stripe runs slowly around the arrow button. The **EVENTS** tab never asks for
attention. A message of another participant is announced by a short clap of a film clapperboard; your own messages
and reactions make no sound. The sound is switched off in **Settings**, section **Sounds**.

The smiley icon in the player bar sends a reaction: for a few seconds it floats over the video for every participant,
with the sender's name. Reactions are available from the moment you enter a session, before a film is chosen. The
layer with the reactions lets clicks through: pause by a click on the video keeps working.

### The pen: circle something right on the video

The **pen** icon in the player bar switches drawing on. The button stays pressed and the cursor over the video turns
into a pen. Hold the left mouse button and move: the line follows the cursor exactly, and every participant sees it at
the same moment. When you release the button, the line stays for two seconds and fades away. Each participant has a
colour of their own, the same as their tile, so it is clear who is drawing. Another click on the icon releases the
button and the cursor is ordinary again.

Lines are stored nowhere and are attached to the picture, not to the window: if your window is wider and the other
person's narrower, the circled place is still the same part of the frame. While the pen is off, clicks on the video
work as usual: pause and full screen.

### Show the photos and videos of a folder (host)

**Show** → **Folder of pictures** (Ctrl+Shift+O) opens a window that shows what lies in a folder: its nested folders,
its pictures with small previews and its videos. Open the folder you need and press **Show**. Under it the window
counts what goes out (pictures, videos, the size of the pictures) and what is left out: camera RAW files and pictures
larger than 64 MB. Two switches go with it:

- **With nested folders**: the files of every nested folder follow those of the folder itself, folder after folder,
  and the strip names a nested folder at its first item.
- **Do not show the place of shooting, the camera and the comments of the pictures** (on by default): the copies the
  participants receive lose the place, the camera, the author, the comments and the extra pictures some phones attach
  after a photo. Your own files are not changed. The orientation stays, so a photo is still shown upright.

In the window **Back** and **Forward** work as in a file manager, with Alt+← and Alt+→, Backspace and the side buttons
of the mouse as well.

At most 2000 files are shown at once. A single picture chosen with **Show** → **File** is shown alone: nothing else of
its folder goes out, and its metadata stays with you.

While a folder is shown, a strip of small pictures lies under the screen. A click on an item puts it on everybody's
screen, like a seek of the film: every participant can do it, the host decides. ← and → go to the previous and the
next item; the button with four squares opens all items as a grid on your screen only (Esc closes it). Under each item
are the marks of those who look at it. The mouse wheel scrolls the strip sideways.

- **Pictures** are drawn by Golether itself. The mouse wheel or the + and − keys enlarge a picture around the pointer,
  a drag moves the enlarged picture, 0 fits it back. That changes only your screen, and the lines of the pen stay on
  their places of the picture. HEIC, AVIF and TIFF photos are turned into JPEG on the host's computer by the video
  component, so participants need nothing for them.
- **Open only for me** (right click on an item) shows a picture on your screen alone. A line on top says what
  everybody else sees and offers **Back to everybody** and **Show to everybody**; the others see the mark "ALONE" on
  your tile. The pen is off meanwhile.
- **Videos** of the folder are played like a film: put on the screen, a video becomes everybody's film with its
  timeline, sound and subtitles, and the strip stays. Its small picture is a frame from the video.

The film pauses when a picture comes on the screen and waits under the pictures; the sound and subtitle buttons are
inactive while a picture is shown. A video of the folder put on the screen replaces the film. **Close the pictures**
(host) ends the showing for everybody, and the film comes back where it was interrupted. Choosing a film with
**Show** → **File** ends it too.

### Waiting until everybody is ready

When you press play, Golether first makes sure that everyone has the file open and its beginning loaded. While
somebody is not ready, the player bar shows who is awaited ("Waiting for: Marina…") and the button turns into "Cancel
the start". The host does not have to wait: **Start without them** starts the film at once, and those behind catch up
by themselves. If a participant is still not ready after 45 seconds, the show starts anyway.

### Continue where you stopped

Golether remembers where you stopped each film (on this computer, in the `data` folder). When the host opens the same
file again, a bar appears above the timeline with the position you stopped at and the **Continue for everybody**
button. A film watched to the end is forgotten.

### What the timeline shows

The slider moves only when the picture really moves: during the countdown before the start and while the player
seeks or waits for data, it stands still.

The light fill on the timeline is what is already loaded and can be jumped to without waiting. For the host and for
anyone watching their own copy of the file, the whole timeline is lit. The circles with letters are the participants:
you can see where each one is.

### The connection broke

If the connection to the host is lost, Golether reconnects by itself: up to eight attempts with growing pauses. No new
invitation is needed: on admission you get a ticket valid for this session. The host still confirms the return, as
with an ordinary entry. While the attempts run, a bar at the top shows the attempt number and the **Leave** button.

### Host: switch off a participant's microphone or camera

Right click on a participant's tile and choose **Switch off the microphone** or **Switch off the camera**. The host
can only switch off: only the participant can switch the device on again, and Golether tells them so. The tiles show
whose microphone or camera is off. Your own tile has no menu: use the buttons under the tiles for yourself.

### Host: remove a participant

The same menu (right click on a participant's tile) has **Remove from the session**. Golether asks you to confirm,
and then the participant leaves the session at once and sees a message that the host removed them. This is not a ban:
they can come back, but only with a new invitation from you; neither the earlier invitations nor the automatic
reconnection let them in. The earlier invitations still work for your other guests: it is the person who is removed,
not the link. Open **Invite**, press **New invitation** and send the new link to that person privately. The same rule
applies to somebody you refused in the window with the verification code.

### Start, end of the film and the space bar

When the film ends, it stops on the last frame for everybody and the play button turns into **Watch again** (a
circular arrow): a press or the space bar starts the film from the beginning for everybody.

The player buttons do not take the keyboard focus, so the space bar always means play/pause, even after a click on
other buttons.

After you press play there is a short countdown: this way all participants start together, even with a large ping.

## What the tiles show

- **Green frame**: the participant is speaking right now (and you yourself when you speak). With the microphone off
  there is no frame.
- **Ping**: the response time to the host. Green below 400 ms, yellow up to 1.5 s, red above.
- **Drift**: how far the participant is ahead of (+) or behind (−) the common position. A small drift is evened out by
  an unnoticeable change of speed, a large one by a seek.
- **Buffer**: how many seconds of video are already loaded ahead. If somebody falls far behind and waits for data,
  the host pauses everybody automatically and continues when all are ready.

When several people watch, participants pass the already loaded parts of the film to each other directly, so the host
does not have to give them to everyone. Each part is checked against the host's checksum. How much was received and
given is shown in the status bar at the bottom of the window.

## If something went wrong

**⋯ → Diagnostic report…** saves a text file into `data/logs` (the **⋯** button appears at the top right when the
window is narrow enough that the buttons of the top bar no longer fit): the version, the state of the components,
information about the session (without the name of the film) and the last lines of the log. Keys, tokens and
passwords are cut out of the report, so the file can be sent to whoever helps you.

At any width of the window the same report is saved from **Settings → About Golether → Diagnostic report…**. Next to
it, **Report a problem** opens a letter to the developers (project.golether@gmail.com) in your mail program, with the
version and the system already filled in; the window of every error offers the same. Attach the report to the letter:
nothing is sent until you send it yourself.

The report has a line for every connection with a participant: whether it is verified, whether the data channel is
open and how many frames went out and came in (`video 120↑/0↓`). They show at once where the link breaks: the
connection did not come up, it came up empty, or the camera gives nothing.

### Nobody can connect: the firewall

Windows treats a new network connection, a tunnel adapter above all, as a public network and drops incoming
connections. Participants then cannot get in even when the computers see each other perfectly. Golether checks this
when a show starts and, if there is no rule, shows the bar **"The firewall does not let participants in"** with the
**Allow** button at the top. The button asks for an administrator's confirmation once and adds one rule for the
program; no permanent service appears, and it never has to be repeated.

The same by hand: "Windows Defender Firewall" → "Allow an app through firewall" → find Golether and tick both the
private and the public network.

## Updates

On start Golether asks the [project page](https://depthrel.github.io/Golether/) whether a new version is out. If it
is, a bar "A new version is available" appears at the top with the **Open the download page** button: your browser
opens the page with the links for your system. Nothing is downloaded or installed by itself: you decide. The **Ignore
this version** tick, set before closing the bar, switches off the reminder about that version; Golether will tell you
about the next one. Personal data is not touched by an update.

In **Settings**, section **Updates**, you can switch the check off or name your own source (the address of a file
with the list of versions, `https://…`); it is needed only for builds of your own.

## Rendezvous server

If you and your participants have "grey" addresses and a port cannot be forwarded, the rendezvous server helps: a
small program on any machine with a public IP (yours or a friend's). It only introduces your computers; the
connection between them is punched directly. The film, the cameras and the voices do not pass through the server,
and it cannot see them.

1. Somebody sets the server up by the
   [administrator guide](../../Sources/Apps/Golether.Web.Server/ADMIN-GUIDE.md) (Windows, Linux or macOS; the same
   file is in every server package).
2. The host chooses the **connection method** on the start screen and creates a session:
   - **Automatically** (the default): Golether learns the address of the project's server from the project page.
     If the page is unreachable, the address from the last time is used; if there is none, the session is created
     without a server and the event feed says so;
   - **Directly**: no server; guests connect only to the addresses of your computer;
   - **My own server**: a field appears for the server address, for example `203.0.113.7:47900`.

   When the server has answered, the event feed shows "Rendezvous server … is ready".
3. A participant has nothing to set up: the server address comes in the invitation. If the direct addresses do not
   answer within 4 seconds, Golether meets the host through the server by itself.

It does not work when at least one side is behind a symmetric NAT (common with mobile operators and in office
networks): Golether then reports that the routers did not let the direct connection through, and what remains is an
AWG tunnel or a forwarded port.

## AmneziaWG tunnel

A tunnel is brought up before connecting: **Settings → AmneziaWG tunnels → Set up tunnels**. During a session this
button is unavailable.

A tunnel is needed when a direct connection does not get through or is blocked. No server of your own is required,
and on Windows **AmneziaWG does not have to be installed separately**: Golether downloads its own engine and brings
the tunnel up in its own instance. If you already run AmneziaWG with addresses of your own, it is not affected: these
are different network adapters.

The first time the tunnel window opens, the engine is downloaded with the **Install** button right in that window
(about 4 MB, no administrator rights needed). Until it is there, the "Bring the tunnel up" buttons are unavailable.

**Host:**
1. In the tunnel window: **I am the host → Create an offer**, send the text to the participant.
2. Paste the participant's answer and press **Accept the answer**. Compare the code by voice.
3. **Bring the tunnel up** (Windows shows a permission prompt, macOS and Linux ask for the password) or **Export
   .conf** for AmneziaVPN.
4. Add the host's address inside the tunnel to the invitation, for example `10.77.41.1:47800` (invitation window,
   **Advanced**).

**Participant:**
1. In the tunnel window: **I am a participant**, paste the offer, **Accept the offer**.
2. Send the answer to the host, compare the code, **Bring the tunnel up**.

**A tunnel lives while Golether is open.** On exit it is brought down automatically; the system asks for an
administrator's permission once. If you refuse, the tunnel stays, and Golether offers to remove it at the next start.
To remove it earlier, use **Bring the tunnel down** in the same window. Tunnels that were not brought up by Golether
are left alone.

Golether finds the public address by itself: when an offer is created and when an answer is accepted, it asks public
STUN servers how its port looks from outside and puts the address into the package. Then both sides "knock" on each
other so that the routers let the answer through. That is why forwarding a port by hand is usually unnecessary.

It does not work when the router gives every destination its own port (symmetric NAT) or the provider keeps you
behind a shared address (CGNAT), on both sides at once. Then one side needs a public IP or a forwarded port. `.conf`
files contain keys: do not publish them.

## Where the data is kept

Golether is self-contained: everything it stores (the device key, the database of contacts and tunnels, `.conf`
configurations, downloaded components, settings) lies in the `data` folder next to the program. An unpacked archive
writes nothing to the user profile or the registry; the Windows installer adds to the registry only the uninstall
entry and the handler of invitation links. Therefore:

- unpack Golether where you have write access: into "Documents", onto the desktop, onto a flash drive. An unpacked
  copy in `Program Files` will not start and asks you to move the folder;
- the Windows installer asks whom to install for. **Only for me** (the default) goes to
  `%LOCALAPPDATA%\Programs\Golether`, needs no administrator rights and keeps the data in `data` inside the
  installation. **For everyone** goes to `C:\Program Files\Golether`, needs administrator rights once during the
  installation, and keeps each user's data in their profile: `%LOCALAPPDATA%\Golether`. The installer also teaches
  the system to open invitation links with Golether. In an unpacked archive links do not open by themselves: use the
  key (on Linux `install-desktop-entry.sh` switches the links on);
- the package for Ubuntu and Debian (`.deb`, installed with `sudo apt install ./Golether-<version>-linux-x64.deb`)
  brings the components for video and cameras with it, puts Golether into the menu, teaches the system to open
  invitation links and keeps each user's data in their profile: `~/.local/share/Golether`. It is removed with
  `sudo apt remove golether`;
- move, copy and delete the `Golether` folder as a whole: all the data travels with it;
- to update an unpacked copy, unpack the new version and move the `data` folder from the old one into it;
- if the data used to be in `%LOCALAPPDATA%\Golether` (early builds), it is moved into `data` automatically at the
  first start;
- on Windows the device key and the tunnel secrets are additionally encrypted for your Windows account. On another
  computer or under another user they cannot be read: the program says so, and after the `data\identity` folder is
  deleted it creates a new key;
- while a folder is shown, `data\gallery` holds the JPEG files the host makes of its HEIC, AVIF and TIFF photos and
  of frames of its videos. It is emptied when the showing ends, or at the next start if the program was closed during
  one;
- `data\identity\device.key` is the device key. If it is deleted, the fingerprint of the device changes and your
  contacts have to compare the code again.

## Frequent questions

- **"The host cannot be reached at any address".** Check that the port is open in the host's firewall, or use a
  tunnel.
- **"The invitation is invalid or has expired".** The lifetime of the invitation is over (a link lives 15 minutes, a
  key up to an hour): ask the host for a new one.
- **"The host removed you" or "The host declined the request".** The earlier invitations no longer let you in; you
  can join with an invitation the host makes after that.
- **macOS does not open the application.** The build is not signed by Apple: "System Settings → Privacy & Security →
  Open Anyway".
- **Another participant has a weak connection.** Their tile gets the "eco" mark: your camera goes to them at a lower
  resolution (320×180) until the connection recovers. The sound does not get worse.
- **No cameras.** Check that the "Cameras and voice" component is installed (start screen) and that Windows lets
  applications use the camera and the microphone ("Settings → Privacy"). A black tile usually means a closed lens
  shutter.
- **The other person hears themselves.** Echo cancellation works for the sound Golether plays; headphones remove the
  problem completely.
