# Personalized YouTube Home backend spike

## Decision

Use a replaceable `IYouTubeHomeClient` boundary. Keep the isolated C# WEB InnerTube client from this spike as the protocol reference and native fallback candidate, but use a maintained `yt-dlp` recommendation adapter as the preferred eventual production transport if its current `:ytrec` behavior is validated at integration time. `yt-dlp` already accepts the application's temporary Netscape cookie file and is maintained specifically against extractor/client drift, including evolving bot-mitigation work. The C# client remains valuable for explicit result semantics, continuation control, targeted diagnostics, and a clean escape hatch if the subprocess route changes.

This does **not** change `IFeedService`, `MockFeedService`, or GTK wiring. The future `IFeedService` migration must become asynchronous, consume `HomeFeedResult`, render only its videos, preserve `ContinuationToken`, and distinguish `RequiresAuthentication` from transient/protocol failures.

## Approaches evaluated

| Approach | Actual personalized Home | Netscape cookies | Continuations | Assessment |
| --- | --- | --- | --- | --- |
| YouTube Data API v3 | No. Its documented resources expose structured public/account data, not the algorithmic web Home feed. | OAuth, not the app's cookie session | Resource-specific page tokens only | Not suitable for this requirement. |
| `yt-dlp` | Its YouTube extractor exposes the `:ytrec` special URL and accepts `--cookies`. | Yes | Extractor-controlled | Best operational fallback because it tracks YouTube changes; adds subprocess/output-contract dependency. |
| WEB InnerTube `browse` | Yes in principle through authenticated `FEwhat_to_watch`. | Yes, after in-memory Netscape parsing | Yes, `continuation` POST payload | Feasible in C#, but intentionally brittle and requires active protocol maintenance. |
| Mobile/TV InnerTube identities | Potentially, but client keys, versions, PO-token/attestation rules and account behavior differ. | Sometimes | Yes | Do not impersonate an alternative client unless WEB proves blocked; increases account and maintenance risk. |
| Existing .NET libraries | No maintained library found that supplies authenticated personalized Home. `YoutubeExplode` targets public metadata/download extraction. | Not for this feature | N/A | Not a solution for personalized Home. |
| JavaScript/Python library at runtime | A maintained extractor can be more resilient than a small local parser. | Usually yes | Library-specific | Use only behind the same interface; do not make it a GTK concern. |

Sources: [YouTube Data API activities.list](https://developers.google.com/youtube/v3/docs/activities/list), [yt-dlp](https://github.com/yt-dlp/yt-dlp), [YoutubeExplode](https://github.com/Tyrrrz/YoutubeExplode), and [YouTube.js InnerTube documentation](https://ytjs.dev/).

## WEB InnerTube protocol

1. With the manual Netscape session held only in memory, request `https://www.youtube.com/` with its YouTube-domain cookies.
2. Extract the public InnerTube API key, WEB client version, and visitor data from bootstrap HTML in memory.
3. POST `https://www.youtube.com/youtubei/v1/browse?key={public-api-key}`.
4. For the first page, send `browseId: "FEwhat_to_watch"`; for later pages, send the returned `continuation` token instead.
5. Parse the response into GTK-independent `HomeFeedResult` and discard unsupported content.

The request body contains a `context.client` with `clientName: "WEB"`, dynamic `clientVersion`, `hl`, `gl`, and visitor data when supplied by bootstrap. The prototype supplies the WEB client name/version request headers, `Origin`, `Referer`, `X-Origin`, and the visitor header when available. `X-Goog-AuthUser` is optional and must only be sent after an eventual account-selection policy exists; the spike does not assume account index zero.

For authenticated WEB requests, cookies alone are not treated as sufficient. The client derives `Authorization: SAPISIDHASH …` from the current Unix timestamp, a YouTube `SAPISID` or `__Secure-3PAPISID` cookie, and the fixed `https://www.youtube.com` origin. Cookie values, derived authorization, visitor data, raw response bodies, and complete headers must never be logged.

## Response shapes and filtering

Initial Home data commonly appears under:

```text
contents.twoColumnBrowseResultsRenderer.tabs[].tabRenderer.content.richGridRenderer.contents[]
```

Continuation responses commonly use:

```text
onResponseReceivedCommands[].appendContinuationItemsAction.continuationItems[]
```

The parser recursively recognizes `videoRenderer`, maps its ID/title/owner/length/thumbnail to `VideoSummary`, and produces canonical watch URLs. `continuationItemRenderer.continuationEndpoint.continuationCommand.token` becomes `HomeFeedResult.ContinuationToken`.

`shortsLockupViewModel` and `reelEndpoint` identify Shorts and are excluded. Ad/promoted renderer keys (for example `adSlotRenderer`, `adPlacementRenderer`, and `promotedVideoRenderer`) are excluded. Shelves, nudges, non-video cards, and unknown renderers are ignored rather than causing failure. Native short filtering stays defensive because renderer shapes can change.

## Brittleness and risk

InnerTube is an undocumented web implementation, not a supported public contract. API keys/client versions/bootstrap fields, JSON renderer hierarchy, anti-bot enforcement, Proof-of-Origin tokens, session validity, geography, and multi-account behavior can change without notice. Automated use of browser-session cookies can also trigger account security controls or violate platform terms. Keep all transport/parser code behind `IYouTubeHomeClient`, surface controlled authentication/protocol errors, and retain the option to switch to the `yt-dlp` adapter without UI changes.

## Verification scope

Verified against public, unauthenticated YouTube bootstrap/configuration and request/response-compatible shapes: WEB client bootstrap fields, the `browse` endpoint, `FEwhat_to_watch`, standard `videoRenderer`, `richGridRenderer`, continuation item shape, and public anonymous Home's history-off nudge behavior.

Fixture-verified only: credential parsing, SAPISIDHASH construction, authenticated request construction, renderer mapping/filtering, continuation parsing, malformed JSON behavior, and authorization-error mapping. No authenticated personal Home response was stored or committed. A live authenticated request was not validated in this spike.

## Next implementation step

After a controlled live validation succeeds, integrate the already asynchronous adapter into the static Home tab with cancellation, loading/signed-out/error states, first-page refresh, and session-change clearing. Keep `MockFeedService` as the explicit development/test backend until then.

## Step 11 adapter and validation gate

`AuthenticatedHomeFeedService` now adapts `IYouTubeHomeClient` to `IFeedService` without GTK dependencies. It exposes asynchronous first-page and continuation operations, maps results to safe application statuses, preserves the continuation token, filters Shorts again defensively, and never falls back to mock data. `HomeSessionValidator` uses that same adapter and returns only a success flag, video count, continuation presence, authentication requirement, and a fixed high-level status message.

No live authenticated validation was attempted in Step 11. `InMemorySessionService` owns manual cookies only inside a running SilverScreen process; there was no reachable running application instance with an active manual session. Consequently no repository-local cookie file was created or required, no personal feed response was read, and the non-negotiable integration gate remains closed. `MainWindow` still uses `MockFeedService` unchanged.

The adapter clears its in-memory Home cache whenever the manual session changes and also clears it for signed-out, empty, rejected, or failed results. It does not persist recommendations. Thumbnail files may remain in the existing public thumbnail cache after sign-out; that privacy limitation remains until cache lifecycle policy is explicitly designed.
