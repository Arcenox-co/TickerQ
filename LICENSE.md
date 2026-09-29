# TickerQ licensing

Copyright 2025-present Arcenox LLC. All rights reserved except as expressly licensed.

This repository contains software under two licensing tracks. The license attached to an immutable released artifact controls; public source visibility and GitHub's fork button do not grant additional rights.

| Software | Versions | Terms |
| --- | --- | --- |
| Schedule A package families listed below | Functional line 5.x and later | [TickerQ Software License Agreement v1.0](licenses/COMMERCIAL.md) |
| TickerQ artifacts originally released under MIT and/or Apache 2.0 | Their original immutable releases | [Preserved open-source terms](licenses/OPEN-SOURCE.md) |
| `TickerQ.MongoDB` | All versions unless its own notice changes | MIT OR Apache-2.0; see [open-source terms](licenses/OPEN-SOURCE.md) |
| TickerQ Hub | Hosted service | Separately contracted terms |

## Commercial package families

The commercial transition applies to these Schedule A package families:

- `TickerQ`
- `TickerQ.Utilities`
- `TickerQ.EntityFrameworkCore`
- `TickerQ.Caching.StackExchangeRedis`
- `TickerQ.Dashboard`
- `TickerQ.Instrumentation.OpenTelemetry`
- `TickerQ.SourceGenerator`
- `TickerQ.SDK`
- `TickerQ.RemoteExecutor`

The transition begins with version `10.5.0` for .NET 10 and parallel versions `9.5.0` for .NET 9 and `8.5.0` for .NET 8 when those immutable artifacts are first distributed as the same functional release. A valid Community, Evaluation, Commercial Subscription, fallback, or other written license from Arcenox LLC is required as specified in the agreement.

- **Repository copy:** [licenses/COMMERCIAL.md](licenses/COMMERCIAL.md)
- **Authoritative public agreement:** https://license.tickerq.net/api/commercial-terms/current/view
- **Agreement version:** `public-master-v1.0`
- **Canonical agreement PDF SHA-256:** `9663da243deba54d8b9a1ca87a3d7e94e4a1a96b744b3a37902307f0456ee640`

## Prior open-source releases

Every immutable TickerQ artifact originally distributed under the MIT License and/or Apache License 2.0 permanently retains those original rights. Nothing in this repository revokes or narrows those grants. The original terms are preserved in [licenses/OPEN-SOURCE.md](licenses/OPEN-SOURCE.md).

## Components outside Schedule A

A package, SDK, hosted service, or other component not listed above is outside the Licensed Product unless its own files, an Order, or an updated Schedule A expressly provide otherwise. Such components remain governed by their own license notices.
