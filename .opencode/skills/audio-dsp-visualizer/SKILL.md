---
name: audio-dsp-visualizer
description: FFT, sample rates, Hann windows, spectrum bands, RMS, peaks, Win2D rendering, latency and DSP golden tests.
---

# Audio DSP + Visualizer

## Rules (active from Phase 6)
- Audio analysis is decoupled from playback: playback thread must never wait for the renderer.
- Drop visualizer frames before dropping audio.
- Never assume 44.1 kHz; use the actual input sample rate.
- FFT sizes 2048/4096, Hann window, log frequency mapping, mono/stereo, RMS/peak/band energy/transients.
- Performance: 60 FPS normal, 30 FPS power saver, <100 ms audio→visual response, adaptive quality.
- Golden tests use synthetic signals: silence, 100 Hz / 1 kHz / 10 kHz, left/right only, white noise, impulse.
- Presets must be independently designed (classic-style but not copies).