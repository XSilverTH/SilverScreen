# SilverScreen Improvement Run Plan

## Dependency graph
- A Lifecycle (App/MainWindow/ApplicationServices) is foundational and independent.
- B Session persistence (Core session contracts + Infrastructure + account UI) precedes C WebKit capture and D invalid-session UX.
- E Structured playback result precedes F direct URL metadata and G playback error polish.
- H Queue synchronization and I libmpv lifecycle are independent, but both precede external playback ownership review J.
- K Feed/status cleanup precedes invalid-session consistency in D.
- L Preferences, M security, N UI polish are independent audits.
- O version/docs and P CI/package are independent of runtime code; P depends on O.
- Final integration verifies all contracts, tests, publish, metadata, and runtime smoke.

## Ownership boundaries
A: App/Shell lifecycle files only.
B: Core/Account session contracts, Infrastructure session stores, account view models only.
C: Account/Auth files only.
D/K: Features/session and feed sources/view models; coordinate shared status vocabulary.
E/F/G: Core playback contracts, routing, search/direct URL and playback callers; E must land first.
H: Queue and PlaybackSession/PlaybackCoordinator plus embedded queue reconciliation.
I: LibMpvPlayer and EmbeddedPlayerView renderer state only.
J: External MPV, IPC observer, temporary cookie file.
L: Preferences files and preference tests.
M: security-sensitive infrastructure/logging and third-party docs.
N: Blueprint/UI files and view polish.
O: release metadata and documentation.
P: workflows/package scripts.

## Integration order
A, B, E, H, I, J, C, K, D, F, G, L, M, N, O, P, then final review and release verification.

Agents must commit focused changes, add behavioral tests or justify manual-only coverage, and run focused verification without project-wide validation.
