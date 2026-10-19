# TickerQ licensing

Copyright 2025-present Arcenox LLC and contributors. All rights reserved except as expressly licensed.

## Current software

Except for the third-party portions identified below, current TickerQ source and package artifacts are governed by the [TickerQ Software License Agreement](LICENSE.md). Public source visibility, downloading a package, and GitHub's platform-enabled forking functionality do not grant operational rights.

TickerQ is switching to commercial, source-available distribution under one agreement. Community, Evaluation, and paid Commercial Subscription are entitlement options within that agreement. This page explains that agreement; it does not grant an additional license.

The following package families are expressly designated as governed by that agreement under Schedule A, including its provision for packages identified by their immutable distribution materials:

- `TickerQ`
- `TickerQ.Utilities`
- `TickerQ.EntityFrameworkCore`
- `TickerQ.Caching.StackExchangeRedis`
- `TickerQ.Dashboard`
- `TickerQ.Instrumentation.OpenTelemetry`
- `TickerQ.SourceGenerator`
- `TickerQ.MongoDB`
- `TickerQ.Integrations`
- `TickerQ.SDK`
- `TickerQ.RemoteExecutor`
- `@tickerq/sdk`

For NuGet packages, the commercial transition begins with functional line 5.x: version `10.5.0` for .NET 10 and parallel versions `9.5.0` for .NET 9 and `8.5.0` for .NET 8 when first distributed as the same functional release. For the previously unpublished npm package `@tickerq/sdk`, the transition begins with version `1.0.0` and includes every later semantic version unless an artifact's immutable distribution materials expressly provide otherwise. TickerQ Hub remains a hosted service governed by separately contracted hosted-service terms.

- **Repository agreement:** [LICENSE.md](LICENSE.md), Version 1.0, dated October 13, 2026
- **Required portal agreement version:** `public-master-v1.0`
- **Required v1.0 PDF SHA-256:** [`licenses/public-master-v1.0.sha256`](licenses/public-master-v1.0.sha256), identifying the reviewed PDF including `TickerQ.Integrations`
- **Current portal agreement endpoint:** https://license.tickerq.net/api/commercial-terms/current/view
- **Previous portal revision:** `public-master-v1.0`, without `TickerQ.Integrations`; PDF SHA-256 `047069f2ef3567cb651303294e1328823a59e7ce02c009496d22322969d50a50`. Its PDF and exact text are archived by digest in the portal repository.
- **Earlier portal PDF SHA-256:** `9663da243deba54d8b9a1ca87a3d7e94e4a1a96b744b3a37902307f0456ee640`

> **Publication gate:** Automated NuGet and npm publication validates the live portal version, exact agreement text, and approved PDF digest against this repository. Publication fails if any value differs or the digest is missing. This initial commercial agreement uses v1.0. A matching version label alone is insufficient: the portal text and approved PDF must match this agreement. Previously published documents and acceptance records retain their original PDF and digest; this publication does not rewrite prior acceptances. The repository and Node SDK package carry identical copies of the same agreement.

## Subscription and expiration behavior

An active paid subscription permits all covered functional lines. After normal expiration, fallback rights cover the anchored line's versions released on or before the end of the paid period, including versions already available at purchase. Package coverage, payment, and the other conditions in Sections 3 and 7 still apply. A later functional line or patch released after the paid period is outside that fallback scope. Renewal establishes a new anchor without removing previously vested fallback rights.

Runtime verification checks certificate authenticity and expiration. It does not verify payment, eligibility, package coverage, or fallback entitlement. Expiration produces warnings without stopping execution, including after an application restart; technical continuity is not permission for unlicensed use. Missing or invalid certificates still block execution. This applies to all entitlement options, and Evaluation never authorizes production use.

The following acceptance scenarios summarize the agreement and implementation. They do not add license terms. The contractual scenarios must also be checked against customer Orders and release records; they are not automated runtime entitlement checks.

| Scenario | Contractual result | Runtime result |
|---|---|---|
| Active paid subscription anchored to 5.x uses a covered 6.x build | Permitted during the subscription | Authentic certificate accepted despite a different anchor |
| Subscription begins after 10.5.0 was released, then expires normally | 10.5.0 qualifies for fallback if covered and paid | Expiration warning; no interruption |
| 9.5.1 and 8.5.1 were released before the paid period ended | Qualify under the same 5.x anchor if covered | Certificate authenticity checked; fallback is established from records |
| 10.5.2 was first released after the paid period ended | Outside that term's fallback rights | Runtime continuity does not authorize its use |
| Subscription anchored to 5.x expires while using 6.x | No 6.x fallback under that term; Section 9 preserves delivered end-user copies | Expiration warning; no interruption or claim of fallback entitlement |
| Community or Evaluation certificate expires, including during a job's preparation or before restart | No fallback; obtain a valid entitlement before further licensed activity, subject to Section 9 | Expiration warning; execution continues |
| Certificate is missing, forged, or structurally invalid | No entitlement established by that certificate | Execution blocked |
| All subscription fees are refunded | No paid period remains to create fallback rights | Local runtime cannot verify refunds |

Before publication, record each customer's covered packages, entities, dates, paid period, and anchor in a retainable Order or entitlement record, and preserve dated release records. The runtime does not promise a separate signed-release or automatic fallback-verification system.

## Runtime certificate setup

For a .NET application running TickerQ, obtain the appropriate certificate through [the licensing portal](https://license.tickerq.net) and install the unmodified `tickerq.tqlicense` file in the host's content root. That is the default lookup location. To use another location, configure it during service registration:

```csharp
builder.Services.AddTickerQ(options =>
{
    options.UseLicense("licenses/tickerq.tqlicense");
    options.UseApplicationNamespace("orders-api");
    options.UseReconciliationEpoch(1);
});
```

Relative paths resolve from the host's content root. Make the file readable by the application identity on every replica, and include it in deployment or mount it from a protected location. Keep customer certificates out of public source control. Certificates are read at startup; restart the host after installing or replacing one. Missing or invalid certificates block job execution, even though the host and Dashboard can start to report the problem. Expiration alone produces a warning and preserves runtime continuity; the contractual limits above still apply.

For a compliant compiled Integrated Product, Section 9 permits the distributor to include its unmodified certificate with the product or deliver it directly for installation with that product. No separate end-user TickerQ license or new certificate type is required solely to run it. End-user terms must restrict the certificate to that product and prohibit sharing it separately. Do not put the certificate in a public repository or offer it as a stand-alone download. Lawfully delivered end-user copies may retain the certificate for the continued runtime use allowed by Section 9. These permissions do not allow new distribution after the distributor's entitlement ends unless the applicable fallback rights permit it.

## Maintaining the published agreement

The portal application is maintained outside this repository. For each future agreement update:

1. Publish the final `LICENSE.md` as the portal's exact UTF-8 agreement text for `public-master-v1.0`, including its whitespace and final newline, and publish a PDF containing the same final terms. Review that PDF before approving it.
2. Preserve any earlier documents and acceptance records. New acceptance records should retain the accepted document, its version and effective date, the accepting customer and individual, the acceptance time, and the PDF SHA-256. Reusing the initial version label does not change an earlier acceptance.
3. Compute the approved PDF's SHA-256, for example with `shasum -a 256 /path/to/approved-public-master-v1.0.pdf`. Update `licenses/public-master-v1.0.sha256` with only the 64-character lowercase digest. Use the PDF that was reviewed and published; do not pin an older portal PDF merely to pass the gate.
4. Run the checks below from the repository root. Both must pass before publishing packages. The portal validator checks exact text, version, and PDF digest; it does not visually compare the PDF's contents with the text.

```bash
python3 -m unittest scripts.tests.test_license_policy -v
python3 scripts/validate-public-terms.py \
  --agreement LICENSE.md \
  --expected-version public-master-v1.0 \
  --expected-pdf-sha256-file licenses/public-master-v1.0.sha256
```

NuGet and npm publication both use this portal check. Local builds and tests can run while the PDF digest is pending.

## Prior open-source artifacts

An immutable TickerQ artifact previously distributed under MIT and/or Apache 2.0 retains the terms included with that artifact. The current commercial agreement does not revoke or narrow those grants.

The final pre-transition source snapshot on the commercial integration line is commit [`b2aefd0932c97d13358c4def46cb5eba92c9b399`](https://github.com/Arcenox-co/TickerQ/tree/b2aefd0932c97d13358c4def46cb5eba92c9b399). Its historical license is available [at that immutable commit](https://github.com/Arcenox-co/TickerQ/blob/b2aefd0932c97d13358c4def46cb5eba92c9b399/LICENSE) and has SHA-256 `4d825c68531c694f10e0aa45ef307aa94b3e22d7a9f0a4dec1086083b50596db`. Published historical package versions retain the license metadata and license files embedded in those package artifacts.

## Third-party portions

Current TickerQ distributions may include retained third-party contributor portions governed by the terms reproduced in [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md). Those notices do not license TickerQ as a whole and do not replace the commercial agreement.
