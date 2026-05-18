# Changelog

## v0.1.1 — 2026-05-18

Auto-reconnect now actually flows audio after the phone's Bluetooth has
been turned off and back on. The previous build often reached
"Streaming from …" without sound, and a manual Disconnect + Connect was
required.

### Fixed

- `AudioPlaybackConnection` is one-shot; on `Closed` the dead session is
  now disposed and a fresh one opened when the device returns, rather
  than left in place to surface a phantom "Streaming" state.
- A scheduled retry loop (1.5 s ticks) replaces the immediate retry that
  raced the Bluetooth teardown and gave up on the first `UnknownFailure`.
  Every connect failure — null `TryCreateFromId`, non-Success
  `OpenAsync`, thrown exception — feeds back into the loop until the
  connection lands.
- After a successful auto-reconnect, Daeron now runs one more
  disconnect → connect cycle (3 s after the first connect settles), so
  the phone's A2DP routing wakes up on the new sink without needing a
  human click.

## v0.1.0 — 2026-05-07

- First sideloadable build (`v0.1.0.16`).
