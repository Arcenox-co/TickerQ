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

For NuGet packages, the commercial transition begins with functional line 5.x: version `10.5.0` for .NET 10 and parallel versions `9.5.0` for .NET 9 and `8.5.0` for .NET 8 when first distributed as the same functional release. For the previously unpublished npm package `@tickerq/sdk`, the transition begins with the first immutable npm artifact distributed with the commercial agreement. TickerQ Hub remains a hosted service governed by separately contracted hosted-service terms.

- **Repository agreement:** [LICENSE.md](LICENSE.md)
- **Authoritative public agreement:** https://license.tickerq.net/api/commercial-terms/current/view
- **Agreement version:** `public-master-v1.0`
- **Canonical agreement PDF SHA-256:** `9663da243deba54d8b9a1ca87a3d7e94e4a1a96b744b3a37902307f0456ee640`

## Prior open-source artifacts

An immutable TickerQ artifact previously distributed under MIT and/or Apache 2.0 retains the terms included with that artifact. The current commercial agreement does not revoke or narrow those grants.

The final pre-transition source snapshot on the commercial integration line is commit [`b2aefd0932c97d13358c4def46cb5eba92c9b399`](https://github.com/Arcenox-co/TickerQ/tree/b2aefd0932c97d13358c4def46cb5eba92c9b399). Its historical license is available [at that immutable commit](https://github.com/Arcenox-co/TickerQ/blob/b2aefd0932c97d13358c4def46cb5eba92c9b399/LICENSE) and has SHA-256 `4d825c68531c694f10e0aa45ef307aa94b3e22d7a9f0a4dec1086083b50596db`. Published historical package versions retain the license metadata and license files embedded in those package artifacts.

## Third-party portions

Current TickerQ distributions may include retained third-party contributor portions governed by the terms reproduced in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Those notices do not license TickerQ as a whole and do not replace the commercial agreement.
