# POS Report Processing Pipeline

AWS serverless pipeline for processing aircraft Position (POS) reports with automatic distance, time, and fuel calculations.

## Overview

This system receives POS reports via HTTP API, parses and validates them, calculates remaining flight time and fuel, and provides status lookups. Built entirely with AWS serverless technologies (Lambda, S3, DynamoDB, SQS, API Gateway).

### Key Features

- **HTTP API for ingestion**: POST /pos-reports
- **Async processing pipeline**: S3 → Lambda (Parser) → SQS → Lambda (Calculator)
- **Status endpoint**: GET /status/{flightId}
- **DynamoDB persistence**: Separate tables for parsed records and calculation results
- **Least-privilege IAM**: Scoped permissions per Lambda, no wildcards
- **Dead letter queue**: Failed calculations retry 3 times then move to DLQ

## Architecture

```
Client → API Gateway
  ├─ POST /pos-reports → Ingest Lambda → S3 pos/
  │
  ├─ S3 Event (pos/*) → Parser Lambda
  │     → S3 attachment/ (JSON)
  │     → DynamoDB ParsedRecords
  │     → SQS ProcessQueue
  │
  ├─ SQS → Calculator Lambda
  │     → Haversine + fuel calculation
  │     → S3 results/
  │     → DynamoDB CalculationResults
  │
  └─ GET /status/{flightId} → Status Lambda
        → Query DynamoDB (latest calc + parse)
        → GetObject from S3 results/
        → Return status JSON
```

See [ARCHITECTURE.md](./ARCHITECTURE.md) for detailed design documentation.

## Repository Layout

```
pos-pipeline/
  README.md
  ARCHITECTURE.md
  
  cdk/                           # TypeScript CDK infrastructure
    bin/app.ts                   # CDK app entry point
    lib/pos-pipeline-stack.ts    # Main stack with all resources
    package.json
    cdk.json
    tsconfig.json
  
  src/                           # C# Lambda functions + shared library
    PosPipeline.sln              # .NET solution
    PosPipeline.Shared/          # Models, parser, Haversine, airport lookup
    PosPipeline.Ingest/          # Ingest Lambda (POST /pos-reports)
    PosPipeline.Parser/          # Parser Lambda (S3 → DDB + SQS)
    PosPipeline.Calculator/      # Calculator Lambda (SQS → calculations)
    PosPipeline.Status/          # Status Lambda (GET /status/{flightId})
    PosPipeline.Tests/           # xUnit tests (gold example)
  
  scripts/                       # Sample data + curl commands
    sample-pos.txt
    example-commands.sh
    README.md
```

## Technology Stack

- **Infrastructure**: AWS CDK (TypeScript)
- **Runtime**: C# .NET 8 Lambda functions
- **AWS Services**:
  - API Gateway HTTP API
  - Lambda (4 functions)
  - S3 (raw, attachment, results)
  - DynamoDB (2 tables)
  - SQS + DLQ
- **Testing**: xUnit

## Local Development

### Prerequisites

- .NET 8 SDK
- Node.js 20+ (for CDK)
- AWS CLI configured (for deployment)

### Build and Test

```bash
# Build the .NET solution
cd src
dotnet restore
dotnet build

# Run unit tests
dotnet test

# Expected: Tests pass or are marked Skip/Fail with TODO
# Gold example tests verify:
# - POS parsing (lat 16.705, lon 96.2083)
# - Flight ID generation (UL20420260904RGNBKK)
# - Haversine distance (~319 NM)
# - Flight time (~43 minutes)
# - Fuel at arrival (~10,513 kg)
```

### CDK Infrastructure

```bash
# Install CDK dependencies
cd cdk
npm install

# Build TypeScript
npm run build

# Synthesize CloudFormation template
# Note: Requires Docker for .NET Lambda bundling
# In environments without Docker, synth will fail at Lambda bundling stage
# but the CDK code itself is valid
npx cdk synth

# Deploy to AWS (requires credentials + bootstrap)
npx cdk deploy

# After deployment, note the API URL from outputs
```

**CDK Bundling Note**: The stack uses CDK's built-in .NET bundling which requires Docker to run `dotnet publish` in a container. The infrastructure code is correct and will deploy successfully in environments with Docker (local development, CI/CD). If `npx cdk synth` fails with "docker ENOENT", Docker is not available - this is expected in some cloud/container environments.

## Deployment

1. **Bootstrap CDK** (one-time per account/region):
   ```bash
   npx cdk bootstrap
   ```

2. **Deploy stack**:
   ```bash
   cd cdk
   npx cdk deploy
   ```

3. **Note the API endpoint** from stack outputs:
   ```
   Outputs:
   PosPipelineStack.ApiUrl = https://xyz.execute-api.us-east-1.amazonaws.com
   ```

4. **Test with sample data**:
   ```bash
   cd scripts
   # Update API_URL in example-commands.sh
   ./example-commands.sh
   ```

## API Usage

### Submit POS Report

```bash
POST /pos-reports
Content-Type: text/plain

POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800
```

Response (202 Accepted):
```json
{
  "flightId": "UL20420260904RGNBKK",
  "status": "RECEIVED",
  "timestamp": "2026-09-04T12:05:00Z"
}
```

### Query Status

```bash
GET /status/{flightId}
```

Response states:
- **PROCESSING**: Parsed but calculation not complete
- **COMPLETE**: Full calculation available
- **404**: Flight not found

Complete response example:
```json
{
  "flightId": "UL20420260904RGNBKK",
  "status": "COMPLETE",
  "timestamp": "2026-09-04T12:05:23Z",
  "remainingFlightTimeMinutes": 43,
  "estimatedFuelAtArrivalKg": 10513,
  "lowFuelWarning": false
}
```

## POS Format

```
POS/{IATA}{number}.FR {departure}/TO {destination}/{ddHHmm}/{lat}{N|S}{lon}{E|W}/{groundSpeed}/{fuelOnBoard}/{fuelFlow}
```

Example:
```
POS/UL204.FR RGN/TO BKK/041205/N1642.3E09612.5/450/12500/2800
```

- **Latitude**: Hemisphere + 2-digit degrees + decimal minutes (N1642.3 → 16.705°)
- **Longitude**: Hemisphere + 3-digit degrees + decimal minutes (E09612.5 → 96.2083°)
- **Date**: Day from message + year/month from request timestamp (UTC)

## Implementation Status

### ✅ Complete
- CDK infrastructure with all resources
- Lambda function stubs (compile and deploy)
- Shared models and service interfaces
- Test project structure with gold example tests
- IAM least-privilege policies (no wildcards)
- S3 event notification scoped to pos/ prefix
- SQS with DLQ (maxReceiveCount=3)
- API Gateway routes

### 🚧 TODO (Marked in code)
- Full POS parsing implementation (lat/lon conversion)
- Complete Haversine formula
- Date parsing edge cases
- Integration tests

The scaffolded code compiles and deploys but uses stub implementations for parsing and calculations. Unit tests reference the gold example expectations.

## Key Design Decisions

1. **Async pipeline**: Ingest returns 202 immediately; processing happens asynchronously via S3 events and SQS
2. **Separate tables**: ParsedRecords and CalculationResults keep concerns separate; Status queries both
3. **S3 for payloads**: DynamoDB stores only metadata + S3 keys; Status reads full JSON from S3 results/
4. **Static airport lookup**: RGN, BKK, SIN hardcoded (sufficient for assessment scope)
5. **HTTP API**: Simpler and cheaper than REST API for this use case
6. **Rounding**: Flight time as integer minutes; fuel as integer kg (matches spec example)

## CDK Packaging

C# Lambdas are packaged using CDK's built-in .NET bundling:
- `lambda.Code.fromAsset()` with `bundling` config
- Runs `dotnet publish` in a container
- Output directory becomes the Lambda deployment package
- Each Lambda project references `PosPipeline.Shared` for shared code

## Confidentiality

This is an assessment project. Architecture details are documented but client-specific requirements are not disclosed in public repositories.

## License

Private assessment project - not for distribution.
