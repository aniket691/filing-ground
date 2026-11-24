# Phase 2 — Backend architecture and implementation plan

Status: **Modules 1–9 are in `backend-dotnet/`.** Legacy `/query`, `/ingest`, `/documents`, and `/reset` are unauthenticated. Chroma is the 1.5.9 server from `ChromaDotNet.Local.ChromaDB`, using `backend/vectorstore/chroma_db` (the same directory as Python `PersistentClient`). Tests use an in-memory store; one test starts the real server on a temp directory and sees `chroma.sqlite3`. Product embeddings and Qdrant are still Module 10. No command has been run against MySQL. The Python and React trees stay untouched.

Decisions already made:

1. Entity Framework Core maps the existing MySQL schema. Migrations stay disabled. No `Migrate()`, no `EnsureCreated()`, no DDL.
2. Legacy HTTP routes and Chroma stay in scope. Something is excluded only after it is named here and you approve the exclusion.
3. New backend folder: `backend-dotnet/`. Later UI folder: `frontend-angular/`.
4. Angular will use real routes, including `/dashboard`, `/overview`, `/documents`, and `/companies`. That work is Phase 3. It is noted here so the API does not assume an in-memory tab.

Runtime target: .NET 10 LTS, ASP.NET Core Web API, C#.

---

## 1. What this API must keep doing

There are two stacks in one Python process. Both stay.

### Product stack (live React UI)

Authenticated with `Authorization: Bearer`. Row access is `user_id`, not roles.

| Area | Routes | Persistence |
|---|---|---|
| Auth | `POST /api/v1/auth/register`, `POST /api/v1/auth/login` | `users` |
| Companies | list, create, get, patch | `companies` |
| Documents | list, create, get, upload, process, status, pages, sections, chunks, tables | MySQL + local files |
| Company documents | `GET /api/v1/companies/{id}/documents` | `documents` |
| Metrics | create, list, get under a company | `financial_metrics` |
| Ingestion stats | `GET /api/v1/ingestion/stats` | in-memory counters |
| RAG | `POST /api/v1/rag/query` | Jina + Qdrant + Groq, filtered by the caller’s document ids |
| Stats | `GET /api/v1/stats/dashboard` and the unversioned alias `GET /stats/dashboard` | counts on MySQL |

Upload rules that are part of the contract: multipart fields `file`, `company_id`, `document_type`, `source_url`, `source_name`; 25 MiB; SHA-256 dedup per user and company; duplicate still returns **201** with the existing document; process returns **202** `{ document_id, status: "queued" }`.

### Legacy stack (Chroma)

Unauthenticated. Used when `CHAT_MODE=rag`. `CHAT_MODE=dummy` (the code default) answers `/query` locally and does not read Chroma. Docker Compose sets `CHAT_MODE` from the environment and defaults it to `rag`.

| Route | Caller today | Chroma? |
|---|---|---|
| `GET /health/live` | Compose-style liveness. Not the Docker healthcheck (that uses `/health`). | No |
| `GET /health` | Live Settings page, dashboard connection pill, Streamlit “Check connection”, unmounted React chat | Yes for `pipeline_ready` and `total_chunks` when the pipeline object exists |
| `POST /query` | Streamlit chat, unmounted `features/chat`, `tests/test_api.py` | Yes when `CHAT_MODE=rag` |
| `POST /ingest` | Streamlit “Load documents”, unmounted `ChatPage`, tests | Yes. Reads `data/raw` or `sample_docs`, chunks, embeds with Jina, writes Chroma |
| `GET /documents` | Tests and the route itself. No current UI calls it. | Yes. Returns collection stats, not the SQL document list |
| `DELETE /reset` | Streamlit “Clear chat & index” | Yes. Deletes the Chroma collection |

`GET /health` is on the live UI. Its body is:

```json
{
  "status": "ok",
  "pipeline_ready": true,
  "total_chunks": 0,
  "llm_model": "openai/gpt-oss-120b",
  "groq_configured": true,
  "jina_configured": true,
  "chat_mode": "rag"
}
```

`pipeline_ready` is true when Chroma’s collection exists, or always when chat mode is `dummy`. `total_chunks` is Chroma’s document count (`get_stats()["total_documents"]`), not a MySQL count. Settings labels that field “Chunks indexed”. The .NET health endpoint has to fill it the same way, which means the legacy Chroma manager has to exist before Settings can be compared.

Startup today constructs `RAGPipeline` inside the process lifetime. If that throws, the API still boots, `pipeline` is null, and chat falls back to dummy. The .NET host should do the same: a Chroma or embedding failure must not take down auth and companies.

### Two embedding clients (do not merge)

| | Legacy `app/rag/embeddings.py` | Product `app/services/embedding_service.py` |
|---|---|---|
| Used by | `/ingest`, `/query` | Document processing and `POST /api/v1/rag/query` |
| Key | `JINA_API_KEY` | `EMBEDDING_API_KEY`, else `JINA_API_KEY` |
| Model field | `JINA_EMBEDDING_MODEL` (`jina-embeddings-v2-base-en`) | `EMBEDDING_MODEL` (same default) |
| Batch size | 100 | `EMBEDDING_BATCH_SIZE` (default 32) |
| Store | Chroma collection `financial_docs` under `backend/vectorstore/chroma_db` | JSON on `document_chunks` plus Qdrant collection `document_chunks` |

Same vendor, different batching, keys, and indexes. Port them as two infrastructure services.

### Product ingestion (not Chroma)

`POST /api/v1/documents/{id}/process` queues `DocumentIngestionPipeline`: validate, load, OCR, clean, sections, chunk, tables, metadata, metrics, persist, then the embedding service writes Qdrant. That path is what the Documents page polls. It does not call `app/rag`.

---

## 2. Active, unused, and candidates to exclude

Nothing in the “candidate” list is dropped in this plan.

**Active — implement.**

- Every `/api/v1` route in section 1, including ones the React shell does not call yet (metrics, sections, chunks, tables, ingestion stats). They are part of the API and have service tests.
- `GET /health`, `GET /health/live`.
- `POST /query`, `POST /ingest`, `DELETE /reset`, because Streamlit calls them.
- Chroma persistent store, legacy loader, chunker, retriever, and Groq generator behind those routes.
- Qdrant retrieval and the v1 RAG answer, including the exact fallback sentence: `I don't have enough information in the provided documents.`
- bcrypt hashes and HS256 JWTs already issued (`sub` is a string user id, `exp`, algorithm and secret from the environment).

**Present, no UI caller — still implement, because you asked not to skip them yet.**

- `GET /documents` (legacy stats shape: `total_documents`, `collection_name`, `embedding_model`, `embedding_api`).
- Unmounted React `features/chat`. The API it needs is the legacy trio above. Whether Angular renders that screen is a Phase 3 question; the endpoints will exist either way.
- `GET /stats/dashboard` without the `/api/v1` prefix. The Vite proxy and nginx can reach it. The live UI uses the versioned path. Both stay.

**Not an ASP.NET module. Left in the Python tree. Not a behavior cut.**

- `backend/legacy/streamlit_app.py`. It is a second UI that already speaks HTTP. Rewriting Streamlit in C# does not preserve the API. The .NET API will keep the routes it calls. The Streamlit file stays where it is.
- `python app/rag/pipeline.py` demo block (`if __name__ == "__main__"`). It is a manual script, not a route.

**Exclusion candidates — waiting for a later yes. Not removed now.**

1. Streamlit as a product surface after Angular covers the same workflows. Until then its endpoints stay.
2. Merging the two Jina clients into one. That would change batch size and which API key `/ingest` uses.
3. Putting a JWT requirement on `/query`, `/ingest`, `/documents`, and `/reset`. They are public today. Adding auth would break Streamlit and the unmounted chat client.
4. Replacing Chroma with Qdrant for the legacy routes. That would orphan `vectorstore/chroma_db` and change `/reset`.

Say if any of those four should be cut. Otherwise they stay.

---

## 3. Solution shape

```
backend-dotnet/
  FinQuery.sln
  src/FinQuery.Api/                 HTTP, middleware, Swagger, composition root
  src/FinQuery.Application/         service interfaces, DTOs, validators
  src/FinQuery.Domain/              entities, domain errors, vocabularies
  src/FinQuery.Infrastructure/      EF Core, files, Qdrant, Chroma, Jina, Groq
  tests/FinQuery.Api.Tests/         WebApplicationFactory contract tests
  tests/FinQuery.Application.Tests/ service tests with fakes
```

Dependency direction: Api → Application → Domain. Infrastructure → Application and Domain. Api references Infrastructure only in `Program.cs` (DI registration). Controllers do not reference EF types.

Why four projects: the Python app already separates routes, services, repositories, and models. Matching that makes each module a port of a folder you already know, and it keeps MySQL and Chroma out of the controllers.

### Projects, in interview language

- **Domain** is the nouns and the rules that are true without HTTP or SQL: a company belongs to a user, a metric needs provenance, processing status is one of a fixed set of strings. Those strings stay strings, not C# enums stored as integers, because the database has no enum types.
- **Application** is the use cases: register, upload, queue processing, answer a question. It talks to interfaces (`IUserRepository`, `IVectorIndex`, `IEmbeddingClient`). It does not know MySQL or Chroma.
- **Infrastructure** is the adapters: EF Core, local disk, Qdrant, Chroma, HTTP calls to Jina and Groq.
- **Api** is the adapter for HTTP: status codes, snake_case JSON, auth header, multipart.

### Cross-cutting pieces (Module 1, then reused)

| Concern | Choice |
|---|---|
| JSON | `System.Text.Json` snake_case. Null fields that Python includes stay. A contract test locks one payload per endpoint. |
| Errors | Middleware maps domain errors to `{ "detail", "error" }` and 401 / 404 / 409 / 422. Model validation uses the same `detail` string-or-list shape the React parser reads. |
| Auth | JWT Bearer. Validation parameters match Python: HS256, same secret, lifetime 30 minutes unless `ACCESS_TOKEN_EXPIRE_MINUTES` overrides, `sub` = user id string. Passwords verified with a bcrypt library that accepts existing `$2b$` hashes. |
| Logging | Built-in `ILogger` with one line per request: method, path, status, milliseconds. No body, token, or password. |
| Config | `IOptions` bound from the current environment names (`AUTH_SECRET_KEY`, `DATABASE_URL` or `MYSQL_*`, `GROQ_API_KEY`, `JINA_API_KEY`, `QDRANT_URL`, `CHAT_MODE`, `STORAGE_PATH`, `BACKEND_CORS_ORIGINS`, …). |
| Swagger | Development only. |
| CORS | Origins from `BACKEND_CORS_ORIGINS`. Methods GET, POST, PATCH, DELETE. |
| Transactions | One `DbContext` per request, `SaveChangesAsync` in the controller filter or a unit-of-work committed by the pipeline after the action succeeds, rolled back on exception. `mark_queued` saves **before** the background job is enqueued, matching the Python early commit. |
| Background ingestion | `Channel` + `BackgroundService` with its own scope. In-process, same durability as FastAPI `BackgroundTasks`: a restart drops a queued job. Not Hangfire, unless you ask later. |
| Database | Pomelo MySQL provider. Connection built from `MYSQL_*` or parsed from `DATABASE_URL`. **Startup does not migrate.** If the database is down, auth routes fail as the Python app would; the process can still serve `/health/live`. |

### EF Core rules

- Fluent configuration names every table and column. `document_chunks.metadata` and `document_pages.extraction_metadata` are mapped explicitly. Embedding vectors are JSON, not a vector column.
- No global query filter that would hide the fact a row belongs to another user. Services pass `user_id` the way repositories do today, so a missing row and a row owned by someone else both surface as not found.
- Case-insensitive uniqueness (`lower(email)`, `lower(legal_name)`) stays in the service. EF will not try to recreate those indexes.
- Tests do not point at `llm_db`. They use either SQLite in a temp file (only if the model can be created there without being applied to MySQL) or Testcontainers MySQL. The first repository test decides which, and that decision is written back into this file. Production configuration never calls schema APIs.

### Legacy and Chroma inside this layout

```
FinQuery.Infrastructure/LegacyRag/
  ChromaVectorStore.cs      persistent client, collection financial_docs
  LegacyDocumentLoader.cs
  LegacyChunker.cs
  LegacyQueryService.cs     dummy mode and rag mode
FinQuery.Api/Controllers/Legacy/
  HealthController.cs       /health, /health/live
  LegacyQueryController.cs  /query
  LegacyIngestController.cs /ingest
  LegacyDocumentsController.cs  GET /documents, DELETE /reset
```

Chroma in .NET: the Python client is `chromadb.PersistentClient` on a local path. The .NET port should open that same directory if a maintained Chroma .NET client can do it without rewriting the on-disk format. If no client can read the existing files, the implementation stops and reports that, with two options for you: (a) run Chroma in server mode and point both stacks at it, or (b) keep calling the Python library through a small sidecar. Guessing a new on-disk layout would orphan indexed data. That check is the first task of the legacy module, before controllers are written.

Qdrant uses the official Qdrant .NET client against `QDRANT_URL`. Empty URL keeps the Python behavior of an embedded path under `backend/vectorstore/qdrant`. The .NET default path will be the same folder, relative to the repo, so an existing local index is visible. Docker keeps using `http://qdrant:6333`.

---

## 4. Module sequence

Each module starts with a short explanation of the Python files, then code, then `dotnet build` and `dotnet test`. The next module does not start in the same step. You can stop me between them.

Database rule for every module: read and write data only. No DDL, no `DROP`, no `ALTER`, no seed that truncates.

### Module 1 — Host, JSON, errors, health skeleton

Python reference: `app/main.py`, `app/api/routes/health.py` (the live probe only), `app/core/errors.py`.

Build the solution, snake_case JSON, exception middleware, request logging, CORS, Swagger, options binding, `GET /health/live`.

`GET /health` in this module returns the same property names. `pipeline_ready` and `total_chunks` use dummy-mode rules until the Chroma module exists: if `CHAT_MODE` is `dummy`, `pipeline_ready` is true and `total_chunks` is 0; if `CHAT_MODE` is `rag` and Chroma is not wired yet, the endpoint still returns 200 and `pipeline_ready: false` rather than crashing. The full Chroma stats land in Module 9. Settings can be compared honestly only after that module.

Tests: live probe; a thrown `NotFoundError` becomes 404 with `detail` and `error`; JSON sample uses snake_case.

No database.

### Module 2 — Users, passwords, JWT

Python reference: `app/models/user.py`, `app/auth/*`, `app/services/auth_service.py`, `app/api/v1/routes/auth.py`, `app/security.py`.

Map `users` only. Register (201), login (200), bearer authentication on a throwaway secured probe that is removed or left as an internal test endpoint only if it does not change the public route table. Public routes stay exactly the Python set, so the probe should be a test-only controller, not a new production route.

Tests, against a disposable database, never `llm_db`:

- Register lowercases email.
- Duplicate email is 409 with the same message.
- Password shorter than 8 or longer than 72 bytes is 422.
- Login failure is always `Invalid email or password.`
- A hash produced by the Python tests’ bcrypt settings verifies in C#.
- A token minted with the shared secret and `sub` as a string is accepted.

### Module 3 — Companies

Python reference: `app/models/company.py`, `app/services/company_service.py`, `app/api/v1/routes/companies.py`, `app/schemas/company.py`.

`GET/POST /api/v1/companies`, `GET/PATCH /api/v1/companies/{id}`. Same query bounds (`q`, `limit` 1–200 default 50, `offset`). Ticker pattern, country length 2, legal name required. Update does not change ticker, exchange, or legal name.

### Module 4 — Dashboard counts

Python reference: `app/api/v1/routes/stats.py`.

`GET /api/v1/stats/dashboard` and `GET /stats/dashboard`. Counts are documents and companies for the caller, and metrics joined through the caller’s companies.

### Module 5 — Document reads

Python reference: list and get in `app/api/v1/routes/documents.py`, `DocumentListRead` (includes `company_name` from display name or legal name).

Also `GET /api/v1/companies/{id}/documents` with `document_type`, `fiscal_year`, `limit`, `offset`.

### Module 6 — Upload and local storage

Python reference: `IngestionService.upload_document`, `DocumentStorageService`, `LocalStorageBackend`.

Multipart upload, 25 MiB, loader allow-list (pdf, html, text — the UI sends pdf, the API accepts more), hash dedup, storage key layout `companies/{id}/{hash12}/{filename}`. Does not run the pipeline. Does not delete existing files.

### Module 7 — Document graph writes and reads

Pages, sections, chunks, tables, metrics. Direct ports of `DocumentService` and `FinancialMetricService`, including provenance checks and normalized metric names. These routes are not on the React shell; they are on the API and covered by `tests/test_api_v1.py`.

### Module 8 — Ingestion pipeline and process endpoint

Python reference: `app/ingestion/**`, `IngestionService.mark_queued`, `run_ingestion`.

`POST /api/v1/documents/{id}/process`, `GET .../status`, `GET /api/v1/ingestion/stats`.

This is the large module. It is split internally, still one review:

1. Status transitions and the background worker, with the pipeline body faked, so 202 and polling work.
2. Loaders and cleaner, compared to Python output on a fixture file.
3. Section, table, and metric detectors, same fixtures.
4. Persist into the existing tables.

OCR follows the Python provider selection. The library is chosen only after reading `app/ingestion/ocr/local.py` in that step, not guessed in this plan.

### Module 9 — Legacy Chroma RAG

Python reference: `app/rag/**`, `app/services/chat.py`, `app/api/routes/{query,ingest,documents}.py`, health stats.

First action is the on-disk compatibility check described in section 3. Then:

- `POST /query` with dummy and rag modes, including the 0.45s dummy delay, 400 when nothing is ingested, 401 when the Groq message contains `expired_api_key` or `Invalid API Key`, 500 otherwise with the exception text (that is the current behavior).
- `POST /ingest`, `GET /documents`, `DELETE /reset`.
- `GET /health` filled from the real Chroma stats.

Question max length on this route stays **500**, not the v1 limit of 4000.

### Module 10 — Qdrant, product embeddings, v1 RAG

Python reference: `embedding_service.py`, `vector_store_service.py`, `retrieval_service.py`, `rag_service.py`, `app/api/v1/routes/rag.py`.

`POST /api/v1/rag/query` scoped to the caller’s document ids. Missing embedding configuration becomes 422, not 500. LLM failure returns the fallback sentence and the sources, HTTP 200.

---

## 5. Contract rules that apply to every controller

- Paths, methods, and success status codes match section 1 and the legacy table. No `/api/v2`. No renamed fields.
- Authenticated v1 routes reject a missing bearer token with 401 and `detail: "Missing authentication token"`.
- A user must not see another user’s companies, documents, or metrics. Unknown id and someone else’s id both look like not found, which is what the repositories do (`get_for_user` returns null).
- Pagination limits that Python rejects with 422 stay 422.
- Dates and decimals: before each DTO is finalized, one Python response is captured and the C# serializer is adjusted to it. No silent switch from a JSON string decimal to a number.

## 6. How a module is verified

- `dotnet build FinQuery.sln` succeeds.
- `dotnet test` succeeds.
- New tests cover the module’s status codes and one JSON body.
- For Modules 2–8 and 10, tests use a disposable database. Connection strings in the test project do not use the Compose MySQL host.
- Manual smoke against a developer database happens only when you ask, and it is limited to inserts the test user owns. No schema statements.

## 7. Angular note (not this phase)

Phase 3 will route `/overview` and `/dashboard` to the same overview screen, plus `/documents`, `/companies`, `/chat`, and `/settings`. Refresh keeps the page. The API does not need new routes for that.

## 8. First implementation step, after you say to start

Module 1 only: create `backend-dotnet/`, the four projects, the test project, health live, error middleware, and snake_case JSON. No MySQL connection is opened in that module.
