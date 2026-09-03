# OLGA Connect NLP Matching API

Standalone .NET 10 Minimal API for deterministic, explainable reciprocal matching. The production endpoint accepts only the requester and context; eligible candidates are retrieved internally by `CandidateRepository`.

## Run locally

Requires .NET 10 SDK. With no `AzureSql` connection string, the API uses an in-memory store and the fake embedding provider. From the repository root:

```bash
dotnet run --project src/Olga.Nlp.Api
curl -X POST http://localhost:5000/v1/matches/search -H 'Content-Type: application/json' -d '{"requestId":"req-1001","memberId":"A123","intentId":"intent-789","contextId":"event-001","limit":7}'
```

Use `docker compose up --build` for the local API/SQL Server topology. Apply `database/001_schema.sql` and `database/002_procedures.sql` to Azure SQL or local SQL Server. Hosted deployments should use Microsoft Entra/managed identity; do not put credentials in source control. Configure `ConnectionStrings__AzureSql` through the environment.

## Endpoints

`POST /v1/matches/search` returns the top 3-7 matches and model, preprocessing, and ranking versions. `POST /v1/matches/score-pair` is an internal evaluation hook. `POST /v1/normalize` is development/admin only. `POST /v1/feedback` persists feedback when the SQL implementation is enabled. `/health` is liveness and `/ready` checks the database.

## Design and deployment notes

EF Core is the default data layer. Candidate retrieval and result persistence are isolated behind repositories, with narrow stored procedure scripts for SQL Server. `LocalEmbeddingProvider` and `AzureEmbeddingProvider` are explicit adapter boundaries; the default fake provider is deterministic for CI and local smoke tests. Add authentication middleware, service identity validation, distributed rate limiting, OpenTelemetry exporters, and a production secrets provider in the hosting environment before exposing the API publicly. Candidate SQL should be extended with the owning application’s presence, visibility, block, connection, suspension, and deletion tables.

## Verification

```bash
dotnet test Olga.Nlp.sln
docker build -f src/Olga.Nlp.Api/Dockerfile .
```

The integration and contract projects are intentionally marked as skipped until a SQL Server/Testcontainers profile is enabled; the unit suite is provider-independent and runs with the fake embedding provider.
