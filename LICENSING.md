# TickerQ licensing

Copyright 2025-present Arcenox LLC and contributors. All rights reserved except as expressly licensed.

## Current software

Except for the third-party portions identified below, current TickerQ source and package artifacts are governed by the [TickerQ Software License Agreement](LICENSE.md). Public source visibility, downloading a package, and GitHub's platform-enabled forking functionality do not grant operational rights.

The following package families are expressly designated as governed by that agreement under Schedule A, including its provision for packages identified by their immutable distribution materials:

- `TickerQ`
- `TickerQ.Utilities`
- `TickerQ.EntityFrameworkCore`
- `TickerQ.Caching.StackExchangeRedis`
- `TickerQ.Dashboard`
- `TickerQ.Instrumentation.OpenTelemetry`
- `TickerQ.SourceGenerator`
- `TickerQ.MongoDB`
- `TickerQ.SDK`
- `TickerQ.RemoteExecutor`
- `@tickerq/sdk`

For NuGet packages, the commercial transition begins with functional line 5.x: version `10.5.0` for .NET 10 and parallel versions `9.5.0` for .NET 9 and `8.5.0` for .NET 8 when first distributed as the same functional release. For the previously unpublished npm package `@tickerq/sdk`, the transition begins with version `1.0.0` and includes every later semantic version unless an artifact's immutable distribution materials expressly provide otherwise. TickerQ Hub remains a hosted service governed by separately contracted hosted-service terms.

- **Repository agreement:** [LICENSE.md](LICENSE.md), Version 1.0, effective September 29, 2026
- **Required portal agreement version:** `public-master-v1.0`
- **Required v1.0 PDF SHA-256:** [`licenses/public-master-v1.0.sha256`](licenses/public-master-v1.0.sha256) — currently `PENDING_PUBLICATION`
- **Current portal agreement endpoint:** https://license.tickerq.net/api/commercial-terms/current/view
- **Currently published portal agreement:** `public-master-v1.0` (different, previously published text; not this revised agreement)
- **Prior portal PDF SHA-256:** `9663da243deba54d8b9a1ca87a3d7e94e4a1a96b744b3a37902307f0456ee640`

> **Publication gate:** Do not distribute an artifact governed by Version 1.0 until the licensing portal publishes matching Version 1.0 PDF and text representations and `licenses/public-master-v1.0.sha256` records the published PDF's exact SHA-256. Automated NuGet and npm publication validates all three values and fails closed while the digest is pending. The existing portal document and this revised agreement share the v1.0 label but are not the same text. A matching version label alone never authorizes publication: exact agreement bytes and the approved PDF digest must also match. Preserve previously published documents and historical acceptance records; this relabeling does not rewrite prior acceptances.

## Prior open-source artifacts

An immutable TickerQ artifact previously distributed under MIT and/or Apache 2.0 retains the terms included with that artifact. The current commercial agreement does not revoke or narrow those grants.

The final pre-transition source snapshot on the commercial integration line is commit [`b2aefd0932c97d13358c4def46cb5eba92c9b399`](https://github.com/Arcenox-co/TickerQ/tree/b2aefd0932c97d13358c4def46cb5eba92c9b399). Its historical license is available [at that immutable commit](https://github.com/Arcenox-co/TickerQ/blob/b2aefd0932c97d13358c4def46cb5eba92c9b399/LICENSE) and has SHA-256 `4d825c68531c694f10e0aa45ef307aa94b3e22d7a9f0a4dec1086083b50596db`. Published historical package versions retain the license metadata and license files embedded in those package artifacts.

## Third-party portions

Current TickerQ distributions may include retained third-party contributor portions governed by the terms reproduced in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Those notices do not license TickerQ as a whole and do not replace the commercial agreement.
