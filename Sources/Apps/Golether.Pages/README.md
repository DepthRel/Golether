# Golether.Pages

The project page: <https://depthrel.github.io/Golether/>. Plain static files with no build step, published by GitHub
Pages from this folder (`.github/workflows/pages.yml`) on every change in the `main` branch.

| File | What for |
|---|---|
| `index.html`, `assets/site.css`, `assets/site.js` | The card for people without technical knowledge: what it is, how to join by invitation, how to invite, where to download. |
| `join/index.html`, `assets/join.js` | The invitation page: turns `https://…/join/#<invitation>` into `golether://join/v1/<invitation>` and hands it to the application. |
| `assets/languages.js`, `assets/flags/*.svg` | The languages of both pages and the language picker with flags; the links to the documentation in the chosen language. |
| `updates.json` | The latest release. The "Download" buttons of the page and the update check of the application both read it. |
| `config.json` | The address of the project's rendezvous server, sealed. The application reads it in the "Automatically" mode. |

## First publication

1. Open the repository on GitHub: **Settings → Pages**.
2. Under **Build and deployment** choose **Source: GitHub Actions**.
3. Push a commit with this folder to `main` (or run the **Pages** workflow by hand on the **Actions** tab).
4. A couple of minutes later the page opens at <https://depthrel.github.io/Golether/>.

## A new release

1. Push a version tag: `git tag 0.3.0; git push origin 0.3.0`. The **Packages** workflow builds
   `Golether-0.3.0-osx-arm64.tar.gz` and `Golether-0.3.0-osx-x64.tar.gz` and attaches them to the release (to a draft
   when there is no release yet).
   Run `Scripts/build-release.ps1` on Windows: it builds the Windows installer and archive, the Linux package and every
   server package, and writes `SHA256SUMS-0.3.0.txt`. Add them to the same release and publish it.
2. In `updates.json` replace the version number in `version` and in the `url` of every system. The optional `notes`
   field is a "what is new" line the application shows.
3. Commit. The page updates itself, and applications of earlier versions say "A new version is available" at start.

`Scripts/App/build-installer.ps1` prints a ready entry for Windows.

## A new picture

The page shows `assets/screenshot.jpg`, a lighter copy of `Docs/images/screenshot.png` (at most 1600 pixels wide).
After replacing the screenshot run `Scripts/Assets/update-site-screenshot.ps1` and commit both files. If the
proportions changed, correct the `width` and `height` of the picture in `index.html`: the script prints the new size.

## The rendezvous server address

`config.json` looks like `{ "rendezvous": "g1.…" }`. The string is made by the owner's utility
`Golether.AdminSettings` (a separate repository): enter `host:port` into it and save the result over `config.json`. An
empty string means "no server": the application creates a session without one and tells the host so.

The sealing is a mask against scanners, not a secret: the key is in the open code of the application
(`Golether.Security.Discovery.EndpointSeal`), and the server address is part of every invitation.

## Rules of the page

- Nothing is loaded from other servers but one thing: the main page counts visits with Umami Cloud (no cookies), its
  script from `cloud.umami.is` and its reports to `gateway.umami.is`. Besides visits it counts the `download` event of
  the download links (`data-umami-event-package`: `windows`, `linux-deb`, `linux-archive`, `macos`;
  `data-umami-event-place`: `card` or `hero`, the button at the top, which `site.js` marks for the system of the
  visitor). It counts only on `depthrel.github.io`, keeps
  the part of the address after `#` and `?` out of its reports and skips browsers that ask not to be tracked. The
  invitation page has no counter: a script of another server would see the invitation in its address. Both pages
  pin what they may load with a `Content-Security-Policy` header; there are no inline scripts or styles either.
- The invitation stands after `#`, so the browser does not send it to the server. The invitation page passes it
  nowhere and removes it from the address bar at once.
- The English text is written right in the HTML (the page reads fine without scripts); the other languages (Deutsch,
  Español, Français, Português, Русский) are tables in `assets/site.js` and `assets/join.js`. Without scripts the
  language picker is hidden.
- The language is chosen like in the application: the one the visitor picked before (kept in `localStorage`),
  otherwise the first language of the browser the page speaks, otherwise English.
- A new language: one more entry in the `list` of `assets/languages.js`, its flag in `assets/flags/<code>.svg`
  (3:2), and one more table in `assets/site.js` and `assets/join.js`. A table must have every key the English page
  has.
- The "User guide" link in the footer leads to the documentation in the language of the page: the `docs` table in
  `assets/languages.js` maps a language to its folder in `Docs/` (`en` → `En`, `de` → `De`, …). A language without a
  translated guide gets the English one; a new documentation language is one more entry in that table.

## Look at it locally

Any static server in this folder, for example `npx http-server . -p 8765`, then <http://127.0.0.1:8765/>. Opening
`index.html` as a file does not work: the security policy of the page allows only its own site.
