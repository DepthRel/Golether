# Golether rendezvous server: administrator guide

This guide lies in the `docs` folder of every server package and suits Windows, Linux and macOS alike. Where the
steps differ, they are given for each system separately. The same guide in Russian:
[ADMIN-GUIDE.ru.md](ADMIN-GUIDE.ru.md).

- [1. What the server does](#1-what-the-server-does)
- [2. What you need](#2-what-you-need)
- [3. What is in the package](#3-what-is-in-the-package)
- [4. Installation](#4-installation)
- [5. Settings](#5-settings)
- [6. Router and firewall](#6-router-and-firewall)
- [7. What to tell the hosts](#7-what-to-tell-the-hosts)
- [8. Managing the service](#8-managing-the-service)
- [9. Checking that it works](#9-checking-that-it-works)
- [10. Updating](#10-updating)
- [11. Removal](#11-removal)
- [12. Security](#12-security)
- [13. Troubleshooting](#13-troubleshooting)

## 1. What the server does

Golether is a program for watching video together. Usually the host and the participants sit behind home routers
without open ports (a "grey" IP, the provider's NAT) and cannot reach each other directly. The rendezvous server
introduces them:

- the host keeps a "room" of its session on the server;
- a participant, coming by the invitation link, asks the server to bring them together with the host;
- the server tells both of them each other's public addresses, and they punch a **direct** connection through their
  routers by themselves;
- on the same port, but over UDP, the server answers STUN requests: this is how cameras and voice connect directly
  as well.

The film, the synchronization, the chat, the cameras and the voice do **not** pass through the server; only short
service messages at connection time do. The load is minimal; one server serves hundreds of simultaneous sessions.

## 2. What you need

| | |
|---|---|
| Network | a public IPv4 address on the machine, or on a router where a port can be forwarded |
| Ports | **TCP 47900** and **UDP 47900** from outside (the number is configurable); port 47980 is local only |
| Windows | Windows 10 / Windows Server 2016 or newer, x64 or arm64 |
| Linux | x64 or arm64 with glibc (Debian 11+, Ubuntu 20.04+, RHEL/Alma/Rocky 8+ and the like) and systemd to run as a service |
| macOS | macOS 14 or newer, Apple silicon or Intel |
| Resources | about 100 MB of disk and 60–100 MB of memory |

.NET does not have to be installed: it is inside the package.

## 3. What is in the package

| File | Purpose |
|---|---|
| `golether-server` (`golether-server.exe` on Windows) | starts the server; the only file in the root; one program runs both in a console and as a service |
| `bin/` | the program itself, the .NET runtime and the libraries; there is no need to look in there |
| `config/appsettings.json` | the settings: address, port, limits (see [section 5](#5-settings)) |
| `config/monitor.json` | the settings of the monitor itself (its language); they never touch the server |
| `docs/ADMIN-GUIDE.md`, `docs/ADMIN-GUIDE.ru.md` | this guide, in English and in Russian |
| `docs/README.txt`, `docs/README.ru.txt` | a short note for this particular package, in English and in Russian |
| `service/install-service.sh` | Linux and macOS: installs, updates and removes the service |
| `service/golether-server.service` | Linux: the systemd unit (`install-service.sh` installs it) |
| `service/org.golether.server.plist` | macOS: the launchd job (`install-service.sh` installs it) |
| `golether-server-monitor` (`golether-server-monitor.exe` on Windows) | starts Golether Server Monitor, next to the server; the monitor itself is in `monitor/`. The monitor: the state of the server and its journal of events in a window, starting and stopping the service, the settings (see [section 9](#9-checking-that-it-works)) |

```
GoletherServer/
  golether-server(.exe)
  golether-server-monitor(.exe)
  bin/        the program and the libraries
  config/     appsettings.json, monitor.json
  docs/       ADMIN-GUIDE.md, ADMIN-GUIDE.ru.md, README.txt, README.ru.txt
  service/    Linux and macOS only
  monitor/    Golether Server Monitor (the files of the program)
```

The Windows installer lays the files out the same way; its own uninstall files are in the `uninstall` folder.

For Windows there is also a separate installer, `GoletherServer-<version>-setup.exe`.

## 4. Installation

Choose the way for your system. In every case the ports have to be opened after the installation
([section 6](#6-router-and-firewall)).

### Windows: the installer (recommended)

Run `GoletherServer-<version>-setup.exe` (administrator rights are required). The wizard speaks English and Russian;
you choose the language in its first window. The installer:

- puts the server into `C:\Program Files\Golether Server`;
- registers the **GoletherServer** service ("Golether rendezvous server"): it starts with the computer and restarts
  5 seconds after a failure;
- adds a Windows firewall rule for the program (TCP and UDP, any port);
- adds shortcuts to the settings, this guide and the status page under "Start → Golether Server".

The service and the firewall rule can be switched off with the ticks on the "Select Additional Tasks" step.

### Windows: from the zip archive

Unpack the archive, for example into `C:\GoletherServer`, and run `golether-server.exe` from the root of the folder:
the server works while the console window is open. To run it as a service, execute in PowerShell **as
administrator** from the root of the server folder:

```powershell
$exe = Join-Path (Get-Location) 'golether-server.exe'
New-Service -Name GoletherServer -DisplayName 'Golether rendezvous server' -BinaryPathName "`"$exe`"" -StartupType Automatic
sc.exe failure GoletherServer reset= 86400 actions= restart/5000/restart/5000/restart/60000
New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program $exe -Action Allow
Start-Service GoletherServer
```

Do not move the folder afterwards: the service starts the program by its full path.

### Linux: a systemd service

```bash
tar -xzf GoletherServer-<version>-linux-x64.tar.gz
cd GoletherServer
sudo ./service/install-service.sh
```

The script copies the server to `/opt/golether-server`, installs the `golether-server.service` unit, enables it at
boot and starts the service. With `ufw` enabled, the script opens the port from the settings itself; for `firewalld`
and `iptables` see [section 6](#6-router-and-firewall). The service runs as a temporary unprivileged user
(`DynamicUser`) with file system protection; the server needs no root rights.

Without a service: `./golether-server` in the root of the package folder (`Ctrl+C` stops it).

### macOS: a launchd service

```bash
tar -xzf GoletherServer-<version>-osx-arm64.tar.gz     # osx-x64 for Intel
cd GoletherServer
sudo ./service/install-service.sh
```

The script copies the server to `/usr/local/golether-server`, removes the quarantine of downloaded files, signs the
program with a local signature (macOS on Apple silicon requires one), installs the job
`/Library/LaunchDaemons/org.golether.server.plist` and starts it. The server runs as the user `nobody` and restarts
after a failure.

Without a service: `./golether-server` in the root of the package folder. If macOS says the program cannot be
opened, run `xattr -dr com.apple.quarantine .` and `codesign --force --sign - golether-server bin/golether-server`
there. On the first start the system firewall of macOS (when it is on) asks whether to allow incoming connections:
allow them.

## 5. Settings

All settings are in `config/appsettings.json` inside the server folder:

| System | Where the file is after installing as a service |
|---|---|
| Windows | `C:\Program Files\Golether Server\config\appsettings.json` |
| Linux | `/opt/golether-server/config/appsettings.json` |
| macOS | `/usr/local/golether-server/config/appsettings.json` |

If the file is missing (deleted by mistake, for example), the server does not fail: it writes it again with the
default settings shown below and says so in its log. If the folder cannot be written, the server starts with the same
defaults all the same. A key missing from the file takes its default value too.

```json
{
  "Rendezvous": {
    "ListenAddress": "0.0.0.0",
    "Port": 47900,
    "EnableStun": true
  },
  "Urls": "http://127.0.0.1:47980"
}
```

| Parameter `Rendezvous:…` | Default | What it sets |
|---|---|---|
| `ListenAddress` | `0.0.0.0` | the IP address **of this machine** to listen on: `0.0.0.0` is every IPv4 interface, a specific address is only that one, `::` is IPv6 and IPv4 |
| `Port` | `47900` | the port: TCP for meetings and the same number over UDP for STUN |
| `EnableStun` | `true` | whether to answer STUN over UDP; without it cameras and voice from different networks connect worse |
| `MaxConnections` | `1024` | connections open at the same time |
| `MaxRooms` | `1024` | sessions running at the same time |
| `MaxPendingPerRoom` | `16` | connections waiting for the answer of one host |
| `MaxConnectionsPerMinute` | `120` | new connections from one IP a minute |
| `MaxStunPerMinute` | `600` | STUN requests from one IP a minute |
| `FirstMessageTimeout` | `00:00:10` | how long to wait for the first message after a connection |
| `AnswerTimeout` | `00:00:20` | how long a host may take to answer a participant's connection |
| `IdleTimeout` | `00:01:30` | how long a host may stay silent before its room is released |

| Parameter | Default | What it sets |
|---|---|---|
| `Urls` | `http://127.0.0.1:47980` | the address of the `/health` status page; keep `127.0.0.1` so that it is visible on this machine only |
| `Logging:LogLevel:Default` | `Information` | how detailed the log is; `Debug` logs every connection |
| `Logging:Console:FormatterOptions:TimestampFormat` | `yyyy-MM-dd HH:mm:ss.fff zzz ` | the stamp at the beginning of every log line in the console and in the file: the date, the server time and the offset from UTC (`2026-10-11 16:14:12.250 +07:00`). Under systemd and in the Windows Event Viewer the time of an entry is set by the system itself |

Any parameter can also be set without editing the file, with an environment variable (a double underscore instead of
the colon) or a command-line argument; they override the file:

```bash
./golether-server --Rendezvous:Port=3478                      # argument, any system
Rendezvous__Port=3478 ./golether-server                        # variable, Linux and macOS
```

```powershell
$env:Rendezvous__Port = '3478'; .\golether-server.exe          # variable, Windows
```

**Restart the service after editing `config/appsettings.json`** ([section 8](#8-managing-the-service)). If a setting
is wrong, the server does not start and lists the errors (in the console or in the service log).

## 6. Router and firewall

**Both** protocols must be reachable from outside on the server port.

**Router.** If the public IP belongs to the router and the server stands behind it, forward to the local address of
the server:

| External port | Protocol | Where to |
|---|---|---|
| 47900 | TCP | `<local IP of the server>:47900` |
| 47900 | UDP | `<local IP of the server>:47900` |

Do not forward port 47980 to the outside. For cloud servers (VPS) open the same ports in the security group or
firewall of the provider's panel.

**System firewall:**

| System | Commands |
|---|---|
| Windows | the installer does it; by hand: `New-NetFirewallRule -DisplayName 'Golether rendezvous server' -Direction Inbound -Program '<server folder>\golether-server.exe' -Action Allow` |
| Linux, ufw | `sudo ufw allow 47900/tcp && sudo ufw allow 47900/udp` (with ufw enabled, `install-service.sh` does it) |
| Linux, firewalld | `sudo firewall-cmd --permanent --add-port=47900/tcp --add-port=47900/udp && sudo firewall-cmd --reload` |
| Linux, iptables | `sudo iptables -A INPUT -p tcp --dport 47900 -j ACCEPT && sudo iptables -A INPUT -p udp --dport 47900 -j ACCEPT` |
| macOS | "System Settings → Network → Firewall → Options": allow incoming connections for `golether-server` (when the firewall is on) |

## 7. What to tell the hosts

Hosts choose **"My own server"** in the "Connection method" switch of the "Show a film" card in Golether and enter
the address of the server into the field that appears, as `address:port`. Participants need to be told nothing: the
address reaches them in the invitation.

(The "Automatically" position of that switch looks for the server of the Golether project itself, not for yours: a
host who wants to use your server has to choose "My own server".)

Give the address at which the server is seen **from the internet**, not `ListenAddress`:

| Where the server stands | What hosts enter |
|---|---|
| A VPS or a machine with a public IP on the interface | that IP (or a domain name): `203.0.113.7:47900` |
| At home behind a router with forwarding | the external IP of the router, **not** `192.168.x.x`: `198.51.100.20:47900` |
| The port was changed | the new port: `203.0.113.7:3478` |

If the external IP of the router changes, get a dynamic DNS name for it and give that.

## 8. Managing the service

| Action | Windows (PowerShell as administrator) | Linux | macOS |
|---|---|---|---|
| State | `Get-Service GoletherServer` | `systemctl status golether-server` | `sudo launchctl print system/org.golether.server` |
| Start | `Start-Service GoletherServer` | `sudo systemctl start golether-server` | `sudo launchctl kickstart system/org.golether.server` |
| Stop | `Stop-Service GoletherServer` | `sudo systemctl stop golether-server` | `sudo launchctl bootout system/org.golether.server` |
| Restart | `Restart-Service GoletherServer` | `sudo systemctl restart golether-server` | `sudo launchctl kickstart -k system/org.golether.server` |
| Log | "Event Viewer → Windows Logs → Application", source `golether-server` | `journalctl -u golether-server -f` | `tail -f /Library/Logs/golether-server.log` |

After a stop with `bootout` on macOS the service is started again with
`sudo launchctl bootstrap system /Library/LaunchDaemons/org.golether.server.plist` (or by a reboot).

The same on Windows from the classic command line (`cmd` as administrator):

| Action | Command |
|---|---|
| State | `sc query GoletherServer` |
| Start | `sc start GoletherServer` |
| Stop | `sc stop GoletherServer` |

The program `Golether Server Monitor` does all of this with buttons and shows what the server is doing; see
[section 9](#9-checking-that-it-works).

**The service comes back by itself.** It starts with the computer, and the installer told Windows to restart it
5 seconds after a failure. Ending the process in the Task Manager counts as a failure, so a minute later the server
is running again. Stop it as a service instead (the commands above, or `services.msc`).

**`golether-server.exe` started from a console says that the port is taken.** Usually it is taken by the service
itself, which is already running. A second copy is not needed: the service does the same work. To run the server in
a console (to see its messages, for example), stop the service first and start it again afterwards.

## 9. Checking that it works

### Golether Server Monitor

The simplest way is the monitor: "Golether Server Monitor" in the Start menu on Windows,
`golether-server-monitor` next to `golether-server` in the server folder on Linux (a desktop is needed) and macOS.
It shows:

- **the state** in words: running as a service, running by hand (not as a service), stopped, the service is not
  installed; how long the server has been running, the rooms, the connections, the meetings and the refusals;
- **the journal of events** in columns: time | IP address | event | details (the host, a tag of the room, why a
  request was refused). The newest events are on top; the search box finds an address, a host, a room or an event;
  the dividers of the header change the widths of the columns; a right click copies the selected rows;
- the buttons **Start** and **Stop** of the service and **Export the journal** to a file;
- the tab **Settings**: the fields of `config/appsettings.json` with explanations, checked by the same rules as at
  the start of the server. "Save and restart" writes the file and restarts a running service.

The language of the monitor is chosen in its window and changes at once. It is remembered in `config/monitor.json`
next to the settings of the server (a file of the monitor of its own: changing it never restarts the server), and every
user may change it. At the first start the monitor speaks the language of the system (English when it does not speak
that one).

Starting, stopping and saving need the rights of an administrator: Windows asks through UAC, Linux through
`pkexec`, macOS asks for the password. Viewing needs no special rights.

The journal is kept in the memory of the server (the last 5000 events) and starts empty with every start of the
server. Nothing of it is written to disk unless you press **Export the journal**: then exactly what the monitor
shows goes into the file you choose. The monitor reads the journal over the status address of this computer; other
computers cannot read it.

### By hand

**On the machine itself**, the status page:

```bash
curl http://127.0.0.1:47980/health                               # Linux, macOS
```

```powershell
Invoke-RestMethod http://127.0.0.1:47980/health                   # Windows
```

```json
{ "status": "ok", "version": "0.3.0", "protocol": 1, "uptimeSeconds": 3600,
  "rooms": 2, "connections": 3, "meetingsRequested": 14, "meetingsIntroduced": 13,
  "refused": 0, "stunAnswered": 211 }
```

| Field | Meaning |
|---|---|
| `rooms` | sessions running with this server |
| `connections` | open connections |
| `meetingsRequested` / `meetingsIntroduced` | how many connections participants asked for / to how many sides the server passed each other's addresses |
| `refused` | refusals because of the limits |
| `stunAnswered` | STUN answers (grows while sessions with cameras run) |

**From outside**, from another machine in another network (for example a phone on mobile internet and a laptop):

```bash
nc -vz 203.0.113.7 47900                                         # Linux, macOS: is the TCP port open?
```

```powershell
Test-NetConnection 203.0.113.7 -Port 47900                        # Windows: TcpTestSucceeded : True
```

UDP is checked with a STUN client, for example `stunclient 203.0.113.7 47900` (the `stuntman-client` package on
Linux). The final check: a host names the server and creates a session, and the event feed of Golether shows
"Rendezvous server … is ready".

## 10. Updating

| System | How to update |
|---|---|
| Windows | run the installer of the new version: it stops the service, replaces the files and starts it again; `config\appsettings.json` is kept |
| Windows, zip | stop the service, replace everything except the `config` folder, start the service |
| Linux, macOS | unpack the new version and run `sudo ./service/install-service.sh` again; `config/appsettings.json` is kept |

Different versions of the server and of the application are compatible while the protocol version is the same (the
`protocol` field of `/health`).

## 11. Removal

| System | How to remove |
|---|---|
| Windows | "Settings → Apps → Golether Server → Uninstall": the service and the firewall rule are removed |
| Windows, zip | `Stop-Service GoletherServer; sc.exe delete GoletherServer; Remove-NetFirewallRule -DisplayName 'Golether rendezvous server'`, then delete the folder |
| Linux, macOS | `sudo ./service/install-service.sh uninstall` from the package folder or from the installed folder |

Do not forget to close the port forwarding on the router.

## 12. Security

- **The server does not see sessions.** Everything that travels between the host and the participants is encrypted
  between their devices; the server knows only IP addresses, the public keys of hosts and the time of connections.
  The server cannot pose as a host: the participant checks the host's key from the invitation.
- **A room cannot be taken over:** it is bound to the device key of the host, and the registration is signed.
- **Minimal rights.** The service on Linux and macOS needs no root rights (a temporary user / `nobody`); on Windows
  the service runs under the system account, like most services.
- **Limits** on the number of connections, rooms and the rate of requests from one IP protect against overload.
  Under attack, lower `MaxConnectionsPerMinute` and `MaxStunPerMinute`.
- **The `/health` page** contains no addresses or names; keep it on `127.0.0.1` all the same.
- **The journal of events** (`/journal`, read by the monitor) holds the IP addresses of the devices, the short
  identifiers of the hosts and tags of the rooms (not their names: a tag does not let anybody reach the host). It is
  answered only to this computer, whatever address the status page has, and lives in memory only.
- The server stores nothing on disk except the log. Only the `config` folder needs a backup.
- Update the server together with the Golether application.

## 13. Troubleshooting

| What you see | What to check |
|---|---|
| The service does not start | the service log ([section 8](#8-managing-the-service)): a wrong setting, or the server cannot listen because the port is taken by another program (`netstat -ano \| findstr 47900` on Windows, `sudo ss -lntup \| grep 47900` on Linux, `sudo lsof -i :47900` on macOS) |
| The server started from a console says that the port is taken, and it comes back after being ended in the Task Manager | the service is already running and is restarted after every failure: stop it as a service ([section 8](#8-managing-the-service)) |
| The host sees that the rendezvous server is unreachable | the service is running; `/health` answers; the TCP port is forwarded and open ([section 9](#9-checking-that-it-works), the check from outside); the host was given the external address, not the local one |
| A participant sees that the host is not on the rendezvous server | the session is still running; the host's event feed said the server was ready; `rooms` in `/health` is above zero |
| The routers did not let the direct connection through | the server did its part (`meetingsIntroduced` grows), but the network of one of the sides does not allow direct connections (a symmetric NAT, common with mobile operators). The server cannot cure this: an AmneziaWG tunnel or a forwarded port on the host's side is needed |
| The film runs, there are no cameras | the UDP port is forwarded and open; `EnableStun` = `true`; `stunAnswered` grows |
| `refused` grows fast | somebody runs into the limits: many participants behind one IP, so raise `MaxConnectionsPerMinute`; suspicious activity, so look at the log at the `Debug` level |
| macOS: the program cannot be opened | in the root of the server folder: `xattr -dr com.apple.quarantine .` and `codesign --force --sign - golether-server bin/golether-server` |
| Linux: `Permission denied` at start | in the root of the server folder: `chmod +x golether-server bin/golether-server service/install-service.sh` (if the archive was unpacked on Windows and carried over) |
