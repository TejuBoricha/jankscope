# Jankscope

Find the real cause of game stutter, wireless-mouse freezes and online-play lag on **any** Windows gaming PC — with evidence, not guesses.

> **Picking this up in a new session (or after a long chat was compacted)? Start with [`CONTEXT.md`](CONTEXT.md) (short, always current), then [`HANDOFF.md`](HANDOFF.md).**
>
> **Status: Stage 0 and Stage 1a (headless core: frame parser, PresentMon runner, stutter detector, hardware probes, synthetic harness) are verified.** Stage 1b has not started. There is no installer or UI yet; see [`docs/STATUS.md`](docs/STATUS.md) for the evidence.

## What it will do
- Explain *why* a frame-time hitch or an input stall happened, with ranked hypotheses, an evidence grade and what would falsify each one.
- Tell device/link-level input problems from system-level stalls using control devices and system timers.
- Characterize network stability honestly, including the limits of ICMP probes.
- Change a setting only when evidence supports it, with a measured before/after and a tested rollback. It is not a "booster": no promised FPS gains, no cleaners.

## Pledges (each is backed by a test or static check)
No telemetry · no network use except probes you approve and links you click · no hooks, injection, overlays or drivers in games · every change logged, approved and reversible · open source (MIT).

## Try it
Requires the .NET 10 SDK ([`docs/INSTALL.md`](docs/INSTALL.md)).
```
./build.ps1
src/Jankscope.Cli/bin/Release/net10.0-windows/jankscope.exe probe
```
`probe` is read-only: it prints which capabilities this PC supports and, for everything it cannot do, says why.

## Documents
[Status](docs/STATUS.md) · [Architecture](docs/ARCHITECTURE.md) · [Decisions (ADRs)](docs/adr/) · [Testing & review](docs/TESTING.md) · [Limitations](docs/LIMITATIONS.md) · [Compatibility](docs/COMPATIBILITY.md) · [Facts register](docs/FACTS.md) · [Third-party components](docs/THIRD_PARTY.md) · [Business notes](docs/BUSINESS.md) · [Risks](docs/RISKS.md) · [Spec](MASTER_PROMPT_v4.md)

## Support the project
A sponsor link will appear here once the maintainer has set one up. Nothing in the app is locked behind payment, and the app never contacts the link.

## License
[MIT](LICENSE). Third-party notices: [`docs/THIRD_PARTY.md`](docs/THIRD_PARTY.md).
