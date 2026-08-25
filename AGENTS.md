# nscreen — agent instructions

Minimal LAN screen streaming: DXGI Desktop Duplication → raw changed rectangles → TCP → an Avalonia
window, plus a UDP probe so nobody types an IP address.

**The server is Windows-x64 only and always will be** — Desktop Duplication has no macOS equivalent.
**The client runs on Windows and on Apple Silicon macOS** from one codebase.

Three projects, and the split is load-bearing:

| Project | TFM | Role | Must never contain |
| --- | --- | --- | --- |
| `src/NScreen.Core` | `net10.0` | Wire formats only: frames, discovery, `RECT` | Any OS call at all |
| `src/NScreen.Server` | `net10.0-windows`, x64 | Captures the primary display and serves it | Window or rendering code — it links neither user32 nor gdi32 |
| `src/NScreen.Client` | `net10.0` | Finds a server, receives frames, draws them | D3D, capture, or **anything Windows-only** |
| `tests/NScreen.Tests` | `net10.0` | MSTest over the wire formats and discovery | A reference to the server project |

`CA1416` (platform compatibility) is deliberately left enabled: in the client and Core it is the
analyzer that catches a Windows-only API sneaking into code that has to run on macOS. If it fires
there, the fix is different code, not a suppression.

Read [README.md](README.md) for what it does and [docs/PROTOCOL.md](docs/PROTOCOL.md) for the wire
format before changing anything that crosses the socket.

## Commands

```bash
dotnet build nscreen.slnx -c Release          # fast framework-dependent build
dotnet build nscreen.slnx -c Debug            # + the full analyzer set (Debug-only by design)
dotnet test nscreen.slnx                      # wire formats, RECT layout, discovery over a socket
pwsh ./publish.ps1                            # -> publish/win-x64/ and publish/osx-arm64/
pwsh ./publish.ps1 -Runtime win-x64           # Windows output only, when iterating
```

The tests stop at the socket. Nothing exercises DXGI, the COM interop or the rendering, so a green
test run says the two sides still agree on the bytes and nothing more.

## Releases

`.github/workflows/ci.yml` runs on a push to `main`, on a pull request against it, and on a `vX.Y.Z`
tag. It runs the four gates below, publishes both platforms, and attaches the archives to a GitHub
release.

| Job | Runner | Runs |
| --- | --- | --- |
| `docs` | ubuntu | `audit-docs.py` |
| `gate` | windows | Debug build with `-warnaserror`, both `dotnet format` checks, `dotnet test` |
| `version` | ubuntu | The number, and whether a release happens at all |
| `win-x64` | windows | `publish.ps1 -Runtime win-x64 -RequireAot`, starts both binaries, zips |
| `osx-arm64` | macos | `dotnet test`, `publish.ps1 -Runtime osx-arm64`, starts the client, tars |
| `release` | ubuntu | Checksums, notes from the commit subjects, `gh release create` |

Tags are the only place a version is kept, and
[.github/scripts/version.ps1](.github/scripts/version.ps1) owns the rules:

| Commits since the newest tag | Next version |
| --- | --- |
| `BREAKING CHANGE`, or `!` after the type | `X+1 . 0 . 0` |
| `feat` | `X . Y+1 . 0` |
| `fix`, `perf`, `refactor`, `build`, `revert` | `X . Y . Z+1` |
| `docs`, `test`, `chore`, `ci`, `style` only | nothing; the build stops after the gates |

Building a tag takes that tag as it stands. That is how `1.0.0` gets declared, and how a patch cut
by hand gets a number. Run the script in a clone to see what the next push would publish:

```bash
pwsh ./.github/scripts/version.ps1
```

Two details the workflow leans on. The Windows job passes `-RequireAot`, so a runner image without
the C++ toolchain fails the build instead of publishing a single-file server. The macOS client is
packed with `tar`, which keeps the execute bit, so a download needs no `chmod`.

Neither binary is signed, and the release notes say so.

Check a change by running it, not by reasoning about it:

```bash
dotnet run --project src/NScreen.Server -c Release
```

```bash
dotnet run --project src/NScreen.Client -c Release
```

Note the two output paths differ — `net10.0-windows` for the server, `net10.0` for the client.

**There is no headless benchmark and no build script.** Both existed and were deleted: the server
already prints its resolution at startup (which proves duplication came up) and `fps` / `Mbit/s`
every second while serving, so a separate bench mode was duplicating the diagnostic it was meant to
provide. Do not reintroduce either without a reason the live output does not already cover.

Three things to know about the end-to-end run: loopback creates a recursive mirror, so the whole
screen changes every frame (a useful worst case, not representative bandwidth); a running exe locks
its own file, so stop it before rebuilding or the build fails with a file-in-use error; and to
confirm the client is really rendering without looking at the user's screen, read its window title —
it carries live fps/bitrate, which proves the receive loop, the cross-thread post and the render
path all work.

**macOS cannot be tested from this repo's usual environment.** `dotnet publish -r osx-arm64`
cross-compiles, and the Mach-O binary it produces was checked here, but nothing has run it. Say so
rather than implying otherwise, and prefer changes whose macOS behaviour follows from the API
contract instead of from platform-specific tricks.

## Style

The `.editorconfig` files and the analyzer set in `Directory.Build.props` are carried over from the
Netrix house configuration (`C:\Users\pavel\Work\wp`). `src/.editorconfig` carries exactly **six**
deviations, each with a comment: four scoped to `Native/` (SDK spelling, file organisation, one
struct declaration) and two project-wide (`SS003`, `SS002`). That number was derived by deleting
every suppression and re-adding only what actually fired. If you add code that trips a new rule,
fix the code first.

**The build is warning-free and clean at `--severity info`. Keep it that way.** After editing C#:

```bash
dotnet build nscreen.slnx -c Debug --nologo -p:TreatWarningsAsErrors=false
```

```bash
dotnet format nscreen.slnx style --verify-no-changes --severity info
```

```bash
dotnet format nscreen.slnx analyzers --verify-no-changes --severity info
```

Prose and Markdown have their own gate, which encodes the machine-checkable half of the
`technical-writing` and `markdown-formatting` skills:

```bash
python .claude/audit-docs.py
```

It checks heading structure, blank lines, fenced-block languages, table cell counts, banned filler,
the Prefer vocabulary, `we` in place of a named actor, and sentence length. It skips
`.claude/skills/`, which holds upstream artifacts that are audited but never rewritten. The rules
needing judgement - active voice, one fact per sentence, whether a comment earns its place - are in
the skills' own review checklists.

Run all four. A plain build shows only `warning`; the `info` tier — where `SS003`, `SS002`,
`IDE0078` and friends live — is invisible without `dotnet format`. Also note an incremental build
does **not** re-run the analyzers, so delete `obj/` when you want a real number. House style is
`var` everywhere and braces on every block; `dotnet format` applies both.

Other conventions:

- Comments explain **why**, and are worth writing where a vtable slot number, a DXGI quirk or a
  backpressure interaction is non-obvious. Do not narrate what the next line plainly does.
- Every file in the repository is in English, including `README.md`.
- File-scoped namespaces, Allman braces, `_camelCase` private fields.

## Skills

`.claude/skills/` carries four writing skills, committed so every clone gets them without a setup
step: `technical-writing`, `markdown-formatting`, `commit-messages` and `docs-linter`. They are
imported from a separate repository (`D:\skills` on the author's machine) and are not
project-specific - nothing in them knows about nscreen.

A skill is model-invoked, and that choice degrades as the skill list grows. `.claude/settings.json`
registers a `PreToolUse` hook, `.claude/hooks/skill-reminder.js`, that names the right skill for the
cases a tool call identifies exactly:

| Tool call | Skill named |
| --- | --- |
| Write or edit of a `.md` / `.mdx` file | `markdown-formatting` and `technical-writing` |
| Write or edit of `.vale.ini` or `styles/<Style>/<Rule>.yml` | `docs-linter` |
| A shell command running `vale` | `docs-linter` |
| A shell command running `git commit` | `commit-messages` |

The hook never blocks a tool call: it exits 0 on any failure and writes nothing where no rule
applies. It reminds once per category per session. Hooks are read at startup, so a change to it
takes effect in the next session, not this one.

Three things worth knowing:

- The copy here fixes a bug in the upstream script: its path patterns accepted `/` only, so on
  Windows - where `file_path` arrives with backslashes - the `docs-linter` rule silently never
  fired. The separator classes are now `[\\/]`.
- The matcher names `PowerShell` beside `Bash`. On Windows a commit often runs through the
  PowerShell tool, and a matcher listing `Bash` alone never saw one.
- `vale` and `markdownlint-cli2` are separate binaries that the skills do not install, and neither
  is present on this machine. The rules those skills teach still apply; only their automated gates
  need the binaries.

`commit-messages` also ships `scripts/commit-msg`, a git gate that checks what commitlint cannot
express: attribution lines, long dashes, an upper-case description, subject length. That directory
holds exactly one hook, so it can serve as `core.hooksPath` directly with no shim:

```bash
git config core.hooksPath .claude/skills/commit-messages/scripts
```

This clone has the gate **on**: the command above is already set here. `core.hooksPath` is per-clone
config rather than a repo file, so a fresh clone runs that command once, by hand. Skip it and
nothing checks a commit message.

The gate rejects `Co-authored-by` and every other attribution line, so commits in this repository
carry none. That overrides the standing instruction an agent has to sign every commit.

## Architecture invariants

Break these and things fail in ways that are hard to see:

1. **`Native/` owns every vtable slot number and struct layout.** Nothing outside `Native/` may know
   that `Map` is slot 14. Read [Native/AGENTS.md](src/NScreen.Server/Native/AGENTS.md) before
   touching COM interop — it has its own rules. Interop struct sizes are a contract: DXGI writes the
   whole struct, so never trim a field to "clean up".
2. **The server has no NuGet runtime dependencies, and that is not negotiable.** Every OS call in it
   is hand-written P/Invoke; reaching for SharpDX or Vortice defeats the point. The client is the one
   exception in the repo: it depends on Avalonia because it has to run on two operating systems. Do
   not let that exception spread — Core stays dependency-free too. Analyzers are Debug-only with
   `PrivateAssets="all"` and never ship.
3. **Neither the capture loop nor the receive loop allocates per frame.** `FramePacket` is reused and
   grows only when a payload needs more room; the client patches the `WriteableBitmap` in place and
   posts a cached `Action` to invalidate. No per-frame `byte[]`, no LINQ, no `string` work in
   `DesktopDuplicator.Grab`, `ScreenServer.Serve` or `FrameReceiver.Run`. LINQ is not used anywhere.
4. **Every frame has the same shape: N rectangles and their pixels.** A whole-screen update is one
   rectangle covering the screen, not a second message type. Reintroducing a "full frame" kind would
   add a branch to the writer, the reader and the renderer to save 16 bytes.
5. **Flow control is TCP backpressure, not a control channel.** A blocked `stream.Write` stops the
   server from calling `AcquireNextFrame`, DXGI coalesces the changes it missed, and the next frame
   arrives coalesced. Adding a send queue, or a frame-rate option, would trade honest frame dropping
   for growing latency.
6. **The server never announces itself.** Discovery is client-driven precisely so an idle server does
   no periodic work: one UDP socket, one thread parked in `recvfrom`, zero CPU. Do not add a beacon
   timer. Discovery must also stay optional — if UDP 7001 is unavailable, frames still flow.
7. **Geometry comes from `IDXGIOutputDuplication::GetDesc`,** not from `IDXGIOutput::GetDesc`, whose
   `DesktopCoordinates` are affected by the process DPI awareness.
8. **The wire format is BGRA32 because that is `PixelFormat.Bgra8888`.** Frames reach the screen with
   a row `memcpy` and no pixel conversion on either platform. Any change to the payload layout pays
   for itself in per-frame CPU on every client.
9. **A child `Control` draws the frame, and the window must not.** `ViewerSurface` overrides
   `Visual.Render`; `ViewerWindow` only sizes itself and handles keys. On macOS with Avalonia
   12.1.1 a `Window` that overrides `Visual.Render` paints nothing — frames decode and the window
   stays white. Neither form needs a theme, so no XAML and no theme package is still the rule.
   Adding an `.axaml` file pulls the XAML compiler and its reflection back in, and `PublishTrimmed`
   stops being safe.
10. **`Protocol` and `Discovery` are the only places a wire layout is written down.** Both sides ship
    in the same repo, so there is no compatibility window to preserve — but any framing change must
    bump the `"NSC1"` magic so a stale build cannot talk to a new one silently.

## Scope

The user asked for a deliberately narrow, resource-frugal tool: primary display only, one client, no
input forwarding, no audio, no authentication, trusted home LAN. Lightness is a stated requirement,
**especially on the server** — the standing instruction is to prefer deleting code to adding options.
The server is down to two flags and the client to one; treat that as the target, not a starting
point. The client carries Avalonia because cross-platform was also a stated requirement; that buys
it a UI toolkit, not licence to grow features.

[docs/ROADMAP.md](docs/ROADMAP.md) records what was left out and why.
