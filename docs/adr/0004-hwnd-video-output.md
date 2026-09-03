# ADR-0004 — Video output: libVLC HWND mode behind the engine

Status: **Accepted** (Phase 1).
Date: 2026-08-30.

## Decision

- Video frames are rendered by libVLC directly into a custom **HWND** owned by a
  `VideoSurface` UI element. The UI passes the HWND to
  `IMediaPlaybackEngine.TrySetVideoWindow(IntPtr)`; all libVLC-specific plumbing stays
  inside `PlaySphere.Playback.LibVLC`.
- No Media Foundation `MediaPlayerElement`, no SwapChainPanel/ISwapChainPanelNative
  interop, and no per-frame copy from libVLC to the UI thread.
- The engine tracks whether a HWND is attached (essential for correct
  EMPTY → LOADING → READY transitions on audio-only media, where no video window exists).

## Consequences

- Zero-copy frame delivery with full libVLC video output filtering (aspect, crop, deinterlace).
- Clean UI separation: WinUI never sees libVLCDisplay; it only hosts a window handle.
- HWND creation must occur on the UI thread and be re-created on session/tab changes
  (Phase 2+); the engine must tolerate `SetVideoWindow` being called once, again, or never
  (audio-only) without deadlocking.
- Requires care with WinUI 3's composition/DirectX on top of a raw HWND-hosted child
  window: overlay precedence is handled by ordering the HWND content before overlays,
  or substituting composition sooner if painting glitches appear.

## Alternatives considered

- `MediaPlayerElement` + Media Foundation: rejected in ADR-0003.
- SwapChainPanel interop with VLC callbacks: more interop surface, no benefit for a
  single-window player.
- VLCDirect video callback + software rendering: simple but loses GPU hardware decode
  and adds a per-frame copy (prohibited for a commercial player).