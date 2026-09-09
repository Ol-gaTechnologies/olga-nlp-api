# OLGA Connect NLP API

.NET 10 API for intent normalization, versioned embeddings, eligibility-aware candidate retrieval, reciprocal ranking, deterministic explanations, feedback, and model evaluation.

## Service boundary

OLGA uses two logical APIs that may share one Azure deployment and Azure Database for PostgreSQL Flexible Server during the MVP. Schema and API ownership remain separate.

The Core Product API owns authentication integration, members, profiles, profile visibility, consent, events, event registration, Live Mode, presence, connections, blocks, chat, files, notifications, privacy workflows, moderation, and administration.

The NLP API owns:

- `nlp.NlpIntent` normalization metadata and lifecycle;
- `nlp.NlpEmbedding`, model versions, ranking versions, and processing jobs;
- idempotent match requests, ranked results, explanations, feedback, suppression, and evaluation;
- bounded candidate retrieval through approved read-only projections.

The NLP API must not update Core Product API tables or infer permission from a score. Production eligibility is read from `nlp.vw_MemberContextEligibility` and `nlp.vw_MemberRelationship`, which the database implementation derives from authoritative IAM, profile, consent, event, Live Mode, connection, and block state. Local InMemory fixtures use stand-alone tables behind the same repository interface.

Cross-API effects use the transactional `ops.OutboxEvent`. NLP emits `NlpIntentNormalized.v1`, `NlpIntentMatchReady.v1`, `NlpMatchRequestCompleted.v1`, `NlpFeedbackRecorded.v1`, and `NlpEvaluationRunCompleted.v1` with minimal metadata and no raw intent, feedback, or evaluation sample text. The Core Product API/its workers consume those events to drive notifications, mobile projections, analytics, privacy, or other product workflows.

## Local development

The Development profile uses EF Core InMemory, deterministic fake embeddings, inline embedding processing, and seeded members. It requires no Azure subscription or PostgreSQL server.

The pull-request and environment deployment process is documented in [CI/CD operations](docs/CI_CD.md).

```powershell
dotnet restore Olga.Nlp.sln
dotnet test Olga.Nlp.sln
dotnet run --project src/Olga.Nlp.Api
```

Create or replace an intent. An existing intent requires the `If-Match` value returned by `GET /v1/intents/{id}`.

```powershell
curl.exe -X POST http://localhost:5000/v1/intents `
  -H "Content-Type: application/json" `
  -H "X-Member-Id: A123" `
  -d '{"intent_id":"new-want","context_id":"event-001","intent_type":"WANT","text":"Need cold-chain storage","expires_at":"2030-01-01T00:00:00Z","category":"cold-chain storage","industry":"pharmaceutical","geography":"Selangor","language":"en"}'
```

Create an idempotent match request:

```powershell
curl.exe -X POST http://localhost:5000/v1/match-requests `
  -H "Content-Type: application/json" `
  -H "X-Member-Id: A123" `
  -H "Idempotency-Key: req-1001" `
  -d '{"request_id":"req-1001","intent_id":"a-want","context_id":"event-001","limit":7}'
```

`B456` should be returned as an explained reciprocal match. `D111` is blocked and must never be returned. Poll or retrieve the reproducible execution with `GET /v1/match-requests/req-1001`.

## Processing model

Normalization is synchronous. The intent row commits original text, PII-minimized normalized text, normalized hash, language, PII signal, preprocessing version, and `PROCESSING` status together.

Development uses `EmbeddingProcessing:Mode=Inline`. Production uses `Queued`, which writes `nlp.NlpProcessingJob`; the worker embeds the stored normalized text, applies bounded retries and a lease, and marks the intent `MATCH_READY`. Normal searches reuse stored vectors and never call the embedding provider.

All WANT and OFFER vectors in a comparison must use the same active model version and 1,536 dimensions. Candidate retrieval is bounded to at most 200 eligible candidates before .NET ranking. PostgreSQL persists embeddings as native `vector(1536)` values; application ranking reuses those stored vectors.

## API contracts

Member endpoints derive identity from the `sub` claim. When the Core Product API calls NLP internally, it may forward `X-Actor-Member-Id` only over the service-authenticated boundary. `X-Member-Id` is accepted only in the Development environment when explicitly enabled.

- `POST /v1/intents`
- `GET /v1/intents/{intentId}`
- `POST /v1/match-requests`
- `GET /v1/match-requests/{requestId}`
- `POST /v1/matches/{matchResultId}/feedback`
- `POST /v1/matches/search` - compatibility endpoint
- `POST /v1/feedback` - compatibility endpoint
- `POST /v1/matches/score-pair` - internal evaluation only
- `POST /v1/normalize` - internal development/evaluation only
- `POST /v1/internal/evaluation-runs` - approved de-identified datasets only
- `GET /v1/internal/evaluation-runs/{evaluationRunId}`
- `GET /health`
- `GET /ready`

Externally retryable mutations use `Idempotency-Key`. Intent updates use `If-Match`/ETag. Errors contain `code`, safe `message`, and `correlation_id`. Responses never expose vectors, raw identity subjects, member presence cells, block direction, provider payloads, or moderation detail.

## Match execution and feedback

`nlp.MatchRequest` stores a canonical request hash, actor, intent/context, language/options snapshot, status, preprocessing/model/ranking versions, threshold, candidate count, and completion state. Reusing a request ID with different inputs returns `IDEMPOTENCY_KEY_REUSED`; an exact replay returns the existing status/results.

Each returned result carries its persisted `match_result_id`, 1-based rank, semantic score, reciprocal score when available, final score, label, deterministic reason codes, and all applied versions. Eligibility and active suppression are checked before ranking; a score cannot bypass them.

Feedback labels are controlled: `USEFUL`, `NOT_USEFUL`, or `INAPPROPRIATE`. Corrections append a new row referencing the immediately previous feedback. They never overwrite history. Free-text feedback is length limited and rejected when contact-style PII is detected.

Evaluation runs require `nlp.evaluate`, the `NLP_EVALUATOR` role, or the authenticated internal-service boundary. They read only `APPROVED` datasets and `TEST` samples, persist the exact model/ranking versions, and return aggregate metrics rather than sample text or private report locations.

## Production configuration

- Configure `ConnectionStrings__PostgreSql` for the PgBouncer endpoint and set `EmbeddingProcessing__Mode=Queued`.
- Implement `AzureEmbeddingProvider` with the approved Azure OpenAI deployment and managed identity.
- Configure `ServiceAuthorization__Token` or replace the service boundary with the approved workload-identity mechanism.
- Keep API/worker/migration database identities separate and grant least privilege by schema/procedure.
- Keep intent text, vectors, identity values, presence, provider payloads, and feedback text out of telemetry.
- Use private endpoints, Key Vault, Application Insights/OpenTelemetry, retry/dead-letter monitoring, and the approved retention/privacy workflows.

The database migrations are intentionally maintained separately. They must implement the v2.3 `nlp` tables and approved projections represented by the EF mappings in this repository.
