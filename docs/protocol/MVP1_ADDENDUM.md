# MVP1 Addendum — PR-04 transport contract clarification

## Why this addendum exists

The original MVP1 plan text names **WebSocket + JSON** for PR-04. Repository implementation and CI coverage currently validate a **TCP framed binary protocol** as the runtime contract.

This addendum resolves that governance drift by documenting current canonical behavior without changing deterministic runtime implementation.

## Current state (canonical)

- Canonical server transport is TCP (`TcpServerTransport` / `TcpEndpoint`) with frame decoding (`FrameDecoder`).
- Canonical message contract is encoded by `Game.Protocol/ProtocolCodec` and `Game.Protocol/Messages` (binary wire format, deterministic decode/encode).
- End-to-end handshake flow in tests is:
  - connect
  - `HelloV2`
  - `Welcome`
  - `EnterZoneRequestV2`
  - `EnterZoneAck` + `Snapshot`

## Non-goals

- This addendum does **not** introduce WebSocket transport.
- This addendum does **not** redefine protocol payloads as JSON.
- This addendum does **not** alter gameplay/simulation logic.

## Governance note

For PR-04, repository code + CI gates are authoritative. Historical roadmap wording remains useful context, but executable contract is the TCP framed protocol currently tested in CI.
