# POS Report Pipeline — Architecture & Design

**Assessment:** Nextstep Engineering — Senior Full Stack / Senior Software Engineer  
**Stack:** C# Lambda handlers + AWS CDK (TypeScript)  
**Deadline:** 13 Oct 2026 (Asia/Bangkok)

## Goal

Receive aircraft POS reports over HTTP, persist raw + parsed data, compute remaining flight time and fuel-at-arrival asynchronously, and expose status lookup by `flightId`.

## High-level flow

```
Client
  │
  ├─ POST /pos-reports (raw text)
  │     → Ingest Lambda
  │         → validate → S3 pos/{flightId}-{now} → 202 { flightId, status: RECEIVED }
  │
  ├─ S3 ObjectCreated (prefix pos/)
  │     → Parser Lambda
  │         → parse POS → S3 attachment/{flightId}-{posTs}.json
  │         → DynamoDB ParsedRecords (PK flightId, SK timestamp)
  │         → SQS ProcessQueue (+ DLQ)
  │
  ├─ SQS message
  │     → Calculator Lambda
  │         → load DynamoDB + S3 attachment
  │         → haversine + fuel estimate
  │         → S3 results/{flightId}-{now}.json
  │         → DynamoDB CalculationResults (PK flightId, SK timestamp)
  │
  └─ GET /status/{flightId}
        → Status Lambda
            → latest ParsedRecords + latest CalculationResults
            → 200 with calc fields if ready, else earlier state / 404
```

## Components

| Piece | Responsibility |
|-------|----------------|
| API Gateway (HTTP API) | `POST /pos-reports`, `GET /status/{flightId}` |
| Ingest Lambda (C#) | Validate raw POS, build `flightId`, write S3 `pos/`, return RECEIVED |
| Reports bucket (S3) | Prefixes: `pos/`, `attachment/`, `results/` |
| Parser Lambda (C#) | S3 event on `pos/` only; parse; write attachment + DynamoDB + SQS |
| ParsedRecords (DynamoDB) | PK=`flightId`, SK=`timestamp` (POS message time), attr `attachment` |
| ProcessQueue (SQS) + DLQ | Decouple parse from calculate; maxReceiveCount → DLQ |
| Calculator Lambda (C#) | Haversine + fuel; write `results/` + CalculationResults table |
| CalculationResults (DynamoDB) | PK=`flightId`, SK=`timestamp` (calc time), attr `attachment` |
| Status Lambda (C#) | Query latest by flightId; GetObject results/; compose status JSON |

Optional: share one Lambda package with multiple handlers, or three projects under `src/`. Prefer separate functions (clear IAM + scaling) with a shared class library for parse/haversine/models.

## Data contracts

### flightId

`{IATA}{number}{yyyyMMdd}{dep}{dest}` — no separators.  
Date = message day + **year/month of request receipt** (UTC).  
Example: `UL204` + `20260904` + `RGN` + `BKK` → `UL20420260904RGNBKK`.

### S3 keys

- Raw: `pos/{flightId}-{requestUtcIso}`
- Parsed: `attachment/{flightId}-{posTimestamp}.json`
- Result: `results/{flightId}-{calcUtcIso}.json`

### Status response states (design)

1. **404** — no rows for `flightId` yet (or never received).  
2. **RECEIVED / PARSED** — parsed row exists, no calc yet → return flightId + latest known timestamp + `status: "PROCESSING"` (or omit calc fields). Spec example only shows post-calc shape; documenting intermediate status is a good senior touch.  
3. **COMPLETE** — calc row exists → return remainingFlightTimeMinutes, estimatedFuelAtArrivalKg, lowFuelWarning, timestamp = calc timestamp.

**Status read path:** CalculationResults (and ParsedRecords) store only `attachment` S3 keys, not the calc payload. Status queries both tables for the latest item by `flightId`, then for a complete response **GetObject** from `results/*` using the CalculationResults `attachment` key and maps those fields into the HTTP response. Do not denormalize calc fields onto DynamoDB (keeps tables matching the brief). IAM: Status needs `dynamodb:Query` on both tables + `s3:GetObject` on `results/*` (and optionally `attachment/*` if we ever expose parse details).

Clarify in README: intermediate responses are intentional (async pipeline).

## POS parsing rules (critical)

Format:
`POS/{IATA}{flightNumber}.FR {departure}/TO {destination}/{ddHHmm}/{lat}{N|S}{lon}{E|W}/{gs}/{fob}/{ff}`

- Lat: hemisphere + 2-digit degrees + minutes (decimal allowed).  
- Lon: hemisphere + 3-digit degrees + minutes.  
- Decimal degrees = degrees + minutes/60; S/W → negative.  
- Timestamp: day+HHmm from message + request year/month → `yyyy-MM-ddTHH:mm:00Z`.

Ingest validation (400): non-empty, starts with `POS/`, enough `/`-separated segments. Full structural parse can stay in Parser (fail → log + no SQS poison if we catch before send; bad objects may need DLQ/manual).

## IAM (least privilege)

| Principal | Allow |
|-----------|--------|
| Ingest | `s3:PutObject` on `arn:.../pos/*` only |
| Parser | `s3:GetObject` on `pos/*`; `s3:PutObject` on `attachment/*`; `dynamodb:PutItem` ParsedRecords; `sqs:SendMessage` ProcessQueue |
| Calculator | `sqs:ReceiveMessage/DeleteMessage/GetQueueAttributes` ProcessQueue; `dynamodb:GetItem` ParsedRecords; `s3:GetObject` `attachment/*`; `s3:PutObject` `results/*`; `dynamodb:PutItem` CalculationResults |
| Status | `dynamodb:Query` both tables (PK only); `s3:GetObject` on `results/*` |

No `*` on resources or actions. S3 notification scoped to `pos/` prefix.

## Failure & retries

- **Ingest:** validation errors → 400; S3 failure → 500 (API Gateway).  
- **Parser:** throw on unexpected errors so S3 can retry (or use destination/DLQ pattern); never send SQS if DynamoDB/S3 attachment failed. Idempotent Put by deterministic keys.  
- **SQS:** visibility timeout > Lambda timeout; `maxReceiveCount` (e.g. 3) → DLQ. Calculator: missing DynamoDB/S3 → fail (retry then DLQ).  
- **Status:** empty query → 404; never invent data.

## Local-first testing

Unit-test without AWS:

1. POS string → structured model + lat/lon decimals (gold: example → 16.705, 96.2083).  
2. flightId builder with frozen “now”.  
3. Haversine + fuel (≈319 NM, ≈0.71 h, ≈10513 kg).  
4. Validation edge cases (missing POS/, wrong parts, S/W signs).

Then `cdk synth` / `cdk deploy` for one E2E path.

## Proposed repo layout

```
pos-pipeline/
  README.md
  ARCHITECTURE.md          # optional; or fold into README
  cdk/                     # TypeScript CDK app (standard layout)
    bin/app.ts
    lib/pos-pipeline-stack.ts
    package.json
    cdk.json
  src/
    PosPipeline.sln
    PosPipeline.Shared/    # models, parser, haversine, airports
    PosPipeline.Ingest/
    PosPipeline.Parser/
    PosPipeline.Calculator/
    PosPipeline.Status/
    PosPipeline.Tests/
  scripts/                 # sample curl, sample POS files
```

CDK packages C# via `DotNetFunction` / bundling (Amazon.Lambda.Tools or container) — document exact approach in README.

## Key decisions & trade-offs

1. **Async pipeline (S3 → SQS → calc)** — matches the brief; ingest stays fast; status may lag (document as normal).  
2. **Two DynamoDB tables** — as required; keeps parse vs calc lifecycles separate; status queries both and takes latest calc when present.  
3. **Shared library** — one parser/haversine implementation for Lambdas + tests; avoids drift.  
4. **Airport lookup in code** — small static dictionary (RGN, BKK, SIN + test airports); no DynamoDB for lookup (YAGNI for assessment). Unknown airport → fail calc to DLQ with clear log.  
5. **HTTP API vs REST API** — prefer HTTP API (cheaper, simpler) unless we need REST-only features; either is fine if documented.  
6. **Rounding** — remainingFlightTimeMinutes as int (spec example 43); fuel as int or round to nearest kg; document rounding rule to match worked example.  
7. **Month-end day edge case** — if message day is `31` and receive month has 30 days, still construct date per brief (use message day + current Y/M); note assumption in README.  
8. **Confidentiality** — private repo; no public gist of the brief.

## Out of scope (unless asked)

- AuthN/AuthZ on API (API key optional stretch).  
- Front-end UI.  
- Real flight computation / weather.  
- Multi-region / X-Ray (nice-to-have logging correlation via flightId in every log line).

## Next implementation order

1. Shared models + parser + haversine + unit tests (gold example).  
2. CDK stack skeleton (bucket, tables, queue+DLQ, four functions, API routes, IAM, S3 notification).  
3. Wire handlers.  
4. Deploy + one curl E2E.  
5. README + diagram.
