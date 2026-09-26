# ADR-0012 — Shell-owned, persistent video surface across navigation

Status: **Proposed** (no implementation in this ADR — see AGENTS.md rule 9: a UI-framework-boundary
change needs this decision on record before any code lands).
Date: 2026-09-26.

## Context

Reported symptom: playing a video, navigating to `HomePage` (or anywhere else) via the shell's
"Now Playing" bar or normal navigation, then returning to `PlayerPage` resets the video to the
start and can leave it looking paused/stalled for a moment.

Root cause (confirmed by code, not guessed): WinUI's `Frame` navigation model tears down and
recreates `PlayerPage` on every navigation. `PlayerPage` hosts a `VideoSurfaceHost` (wrapping
libVLC's `VideoView`, a `SwapChainPanel`), and a brand-new `VideoSurfaceHost` means a brand-new
native swap-chain. `LibVlcPlaybackEngine.AttachVideoSurfaceAsync` only carries a *video* session
(not audio-only) into `RebuildRendererCore` when the swap-chain options actually change — which
they do on every re-entry, because the surface itself is new. `RebuildRendererCore` best-effort
carries the playback position onto the freshly-rebuilt native player, but per the extensive prior
investigation recorded in AGENTS.md, **setting a fractional/absolute position on a libVLC player
before it starts playing is silently dropped**, and every mechanism tried to force it (`:start-time`
media option, setting `Time` inside the native `Playing` callback) reproduced a real, uncatchable
`AccessViolationException` in this codebase. So the current, deliberately-accepted behavior is
"best-effort resume, may restart near the beginning" for video specifically. (Audio-only sessions
already avoid this entirely via a separate fast path that skips the rebuild, since there is no
picture to re-attach — see the "Audio-only fast path" entry in AGENTS.md.)

The actual fix is not another attempt at forcing position restoration on a rebuilt renderer — that
avenue has been tried and has a confirmed crash history. The fix is to **stop rebuilding the
renderer at all** for this case, by not tearing down the swap-chain surface on ordinary navigation
away from and back to the player.

## Constraints

- AGENTS.md rule 9: changing UI-framework boundaries (how navigation owns/tears down pages) needs
  an ADR first — this is that ADR.
- No native call sequencing in the already-fragile `RebuildRendererCore`/audio-callback path should
  be touched by this change. The goal is to make that path fire *less often* (ideally never, for
  this specific case), not to change what it does when it does fire.
- This environment has no interactive desktop UI automation, so whatever is built here needs the
  same manual-QA gate every other audio/video feature in this codebase has — verify via existing
  regression tests (`VideoSurfaceTests`) plus new ones for "surface reused, not rebuilt, on
  re-entry," then a manual pass.
- Must not regress the already-working audio-only fast path, fullscreen/cinema/compact-overlay
  modes, or the visualizer's own window-change pause/resume handling (`PauseVisualizerAcrossWindowChange`).

## Options considered

### Option A — Keep `PlayerPage` itself alive across navigation (cache the Page instance)

Some apps configure their `Frame`/navigation cache so a specific page type is reused rather than
recreated (`NavigationCacheMode.Required` in WinUI). Rejected as the primary approach: it caches the
whole page (all its drawers, event subscriptions, timers, the stereo-balance session wiring, window-
mode state) rather than just the piece that actually needs to persist (the video surface), so it
carries a much larger blast radius for a fix that only needs to solve one thing — the swap-chain
surface identity. Every one of `PlayerPage`'s many `Page_Unloaded`/`OnNavigatedTo`-keyed behaviors
(there are several — window-mode exit, timer teardown, stereo-balance session stop, bookmark
list refresh gating) would need re-auditing for "what does this mean if the page never truly
unloads," which is a much bigger review surface than the actual bug calls for.

### Option B — Shell-owned, persistent `VideoSurfaceHost` singleton (chosen direction)

Move ownership of a single `VideoSurfaceHost` instance up to `AppShell` (which already outlives
page navigation — it hosts `ContentFrame` and the "Now Playing" bar). `PlayerPage` no longer
constructs its own `VideoSurfaceHost` in XAML; instead it borrows the shell's shared instance on
`OnNavigatedTo` (re-parenting it into its own video-surface grid cell) and returns it to the shell
(or simply stops referencing it, since the shell keeps the reference) on `Page_Unloaded`. Because
the same `VideoSurfaceHost`/swap-chain object survives the page teardown/recreation cycle, its
`SwapChainOptions` never change across a simple "go home and back," so
`AttachVideoSurfaceAsync`/`RebuildRendererCore`'s rebuild condition (`swapChainOptions differ from
current`) is never triggered for this case — playback simply continues uninterrupted, exactly like
the existing audio-only fast path already does, but via "the surface never changed" rather than
"there is no surface to reattach."

Trade-offs:
- Re-parenting a `UserControl` between two different visual trees (a page's grid and the shell's) is
  a well-supported WinUI operation (`panel.Children.Remove`/`Add`), not exotic, but it does need
  care around timing — the control must be removed from the old parent before being added to the
  new one, and `PlayerPage`'s existing `VideoHost_Initialized`/attach-on-ready flow needs to account
  for "the surface may already be initialized from a previous page instance" (it currently assumes
  first-time init on every page load).
  - Every non-player page now indirectly "contains" a hidden video surface via the shell — needs a
  clear, simple visibility rule (hidden/zero-size when not on `PlayerPage`) so it never visibly
  leaks or captures input elsewhere.
- Scoped blast radius: only the video-surface hosting/attach code changes; `PlayerViewModel`, the
  drawers, window-mode handling, and the stereo-balance session (just wired in a prior session)
  are untouched.
- Memory note: the surface (and its native swap-chain resources) now live for the app's whole
  session once first created, not just while `PlayerPage` is loaded. Given this is a single-window
  desktop media player where the player is a primary, frequently-revisited surface (not a rare
  screen), this is an accepted trade-off, not a real regression — the swap-chain panel itself is
  lightweight when not rendering (no media attached = no decode happening).

### Option C — Leave it as an accepted limitation

Rejected per the discussion that led to this ADR: the bug fires on the single most common
navigation pattern in the app (go home, come back during playback), which makes it a poor candidate
for "accepted limitation" compared to the app's other, genuinely rarer documented limitations
(stream reconnect edge cases, mid-playback visualizer toggle on non-Windows).

## Decision

**Adopt Option B.** Move `VideoSurfaceHost` ownership to `AppShell` as a single, app-lifetime
instance; `PlayerPage` borrows and re-parents it on navigation instead of constructing its own.
`AttachVideoHostAsync`'s existing "already initialized" handling (AGENTS.md notes this case was
already anticipated: "If the video surface already initialized before navigation ... attach now")
is the natural hook point — it already exists to handle a re-used, already-ready surface, it just
needs the surface itself to actually be the same instance across page loads for that path to be
exercised by the common "return from Home" case instead of only rare timing edge cases.

Implementation is intentionally **not** part of this ADR — this document authorizes the direction;
the follow-up ticket does the re-parenting logic, updates `AppShell.xaml`/`AppShell.xaml.cs` and
`PlayerPage.xaml`/`PlayerPage.xaml.cs`, and adds the regression tests named above.

## Consequences

- `PlayerPage.xaml` no longer declares its own `<controls:VideoSurfaceHost x:Name="VideoHost" .../>`
  — it references the shell's instance instead (exact mechanism — a shared resource, a property on
  `AppShell` the page reads, or a small DI-registered "current shell" accessor — is left to the
  implementation ticket).
- `AppShell` gains a lifetime responsibility it didn't have before (owning a libVLC-adjacent native
  control), which is a small widening of the shell's role — acceptable since the shell already
  manages the one other cross-page playback surface (the Now Playing bar).
- The existing `RebuildRendererCore` position-carry code, its documented "may restart near start"
  limitation, and the AV-crash history around forcing precise position restoration are **unchanged**
  and remain accurate for the cases where a rebuild genuinely is needed (a real file switch, a
  fullscreen/cinema-mode swap-chain option change, etc.) — this ADR only removes the *unnecessary*
  rebuild that happens purely from ordinary page navigation.

## Verification (for the follow-up implementation ticket, not this ADR)

- Existing `VideoSurfaceTests` (the attach/detach/rebuild contract against the real
  `LibVlcPlaybackEngine`) must continue passing unmodified.
- New test(s): re-entering the player via the "Now Playing" bar / engine-parameter navigation path
  does NOT change `SwapChainOptions` and does NOT trigger `RebuildRendererCore` when nothing else
  requires it.
- Manual QA (standing limitation, no desktop automation here): play a video, navigate Home, return,
  confirm playback continues from where it was with no reset and no pause; confirm fullscreen/
  cinema/compact-overlay mode transitions still work; confirm a genuine new-file load still rebuilds
  correctly; confirm no visible/duplicate video surface appears on non-player pages.
