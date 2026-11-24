# Phase 1 — Migration analysis (FinanceIQ / FinQuery)

Status: **approved as analysis**. Backend implementation is planned in `docs/PHASE2_BACKEND_PLAN.md`. That plan keeps the legacy HTTP routes and Chroma path; it does not drop them.

Product: FinanceIQ (FinQuery), a per-user financial-filing workspace. Users register, keep a private company directory, upload PDFs, run an ingestion pipeline, and ask grounded questions with page citations.

| Layer | Today | Target (proposed, not built) |
|---|---|---|
| UI | React 19 + TypeScript + Vite + Tailwind CSS 4 | Angular (latest stable at Phase 3) + TypeScript, standalone components |
| API | FastAPI 0.115, Pydantic 2, Python | ASP.NET Core Web API on .NET 10 |
| Relational data | SQLAlchemy 2 + Alembic, MySQL 8.4 (SQLite for tests) | Entity Framework Core against the **existing** MySQL schema. No EF migrations. |
| Vectors | Qdrant 1.12 (legacy path also has Chroma) | Keep Qdrant. It is not part of the SQL schema. |
| Files | Local filesystem (`STORAGE_PATH`) | Same volume and key layout |
| AI | Jina embeddings (HTTP), Groq via LangChain | Same providers, called from C# over HTTP |

New code will live beside the current trees (`backend/`, `frontend/`). Nothing in those folders will be deleted or overwritten.

---

## 1. Current architecture

```
Browser
  React SPA (Vite :5173 dev, nginx :8080 prod)
    AuthContext + localStorage key "financeiq_auth"
    fetch wrapper in frontend/src/services/api.ts
        |
        |  Authorization: Bearer <JWT>
        v
  FastAPI (uvicorn :8000)
    /api/v1/*     authenticated product API  <-- the live UI uses this
    /health       unauthenticated liveness used by Settings and the dashboard
    /query /ingest /documents /reset   legacy RAG, no JWT  <-- not mounted in the live UI
        |
        +-- MySQL 8.4   users, companies, documents, pages, sections, chunks, metrics, tables
        +-- Qdrant      chunk vectors (collection document_chunks, 768-d)
        +-- Local disk  uploaded files
        +-- Jina        embeddings
        +-- Groq        grounded answers
```

There are **two backends inside one process**:

1. **Product API (`app/api/v1`)** — JWT, user-scoped rows, SQLAlchemy repositories, ingestion pipeline, Qdrant retrieval. This is what the dashboard calls.
2. **Legacy RAG (`app/api/routes` + `app/rag`)** — Chroma + LangChain, `CHAT_MODE=dummy|rag`, no authentication. Started in `lifespan` in `app/main.py`. The React chat feature that calls it (`features/chat`) is **not imported by `App.tsx`**.

`backend/legacy/streamlit_app.py` is an older UI. It is not part of the React app.

Docker Compose (`docker-compose.yml`) runs MySQL 8.4, Qdrant 1.12.4, the FastAPI container, and an nginx frontend on port 8080. Alembic runs on backend boot. CI is pytest plus frontend lint/build (`.github/workflows/ci.yml`).

---

## 2. Frontend (React)

### How navigation works

There is **no React Router**. `App.tsx` shows `AuthScreen` or `DashboardPage`. `DashboardPage` switches panels with `useState<NavItemId>` (`dashboard | documents | companies | chat | settings`). Refreshing the browser always returns to Overview. URLs do not identify the page.

`pages/HomePage.tsx` and `components/StatusRow.tsx` are unused. `features/chat/ChatPage.tsx` is exported and unused. The live “Chat / Ask” item renders an ask panel **inside** `DashboardPage`, which calls `POST /api/v1/rag/query` through `services/api.ts`, not the legacy `/query` client.

### Pages that users actually see

| Screen | File | Behavior |
|---|---|---|
| Sign in / register | `features/auth/AuthScreen.tsx` | Email + password. Register then immediately logs in. Password hint: at least 8 characters. Show/hide password. |
| Overview | `features/dashboard/DashboardPage.tsx` | Counts, quick ask, three suggested questions, five newest documents. |
| Documents | `features/documents/DocumentsPage.tsx` | PDF upload, process, status polling, page viewer. |
| Companies | `features/companies/CompaniesPage.tsx` | Search, create, inline edit. |
| Ask | same dashboard file, `active === 'chat'` | Question box, answer, source chips (document id, pages, score). |
| Settings | `features/settings/SettingsPage.tsx` | Email, active flag, user id, health flags, logout. |

Shell pieces: `DashboardSidebar`, `DashboardHeader`, `shared/components/Header.tsx`, `shared/components/Logo.tsx`.

### State

No Redux, Zustand, or React Query. Each screen owns `useState`. Server data is loaded in hooks with `useEffect`:

| Hook | Calls |
|---|---|
| `auth/AuthContext.tsx` | login, register, logout, 401 handler |
| `auth/session.ts` | `localStorage["financeiq_auth"]` = `{ token, user }` |
| `hooks/useDocuments.ts` | `GET /v1/documents` |
| `hooks/useCompanies.ts` | company options for the upload dropdown |
| `hooks/useDashboardStats.ts` | `GET /v1/stats/dashboard` |
| `hooks/useBackendHealth.ts` | `GET /health` |
| `hooks/useSystemHealth.ts` | `GET /health` (settings, refreshable) |
| `features/documents/hooks/useDocumentPages.ts` | `GET /v1/documents/{id}/pages` |
| `features/documents/components/ProcessButton.tsx` | `POST .../process` then poll `GET .../status` |
| `features/chat/hooks/useChat.ts` | localStorage chat sessions + legacy `/query` (**unused by the shell**) |

### API client

`frontend/src/services/api.ts` is the only client the live UI uses.

- Base URL: `VITE_API_BASE_URL` or `/api`.
- Attaches `Authorization: Bearer <token>` from localStorage.
- On **401** outside `/v1/auth/*`, clears the session and notifies `AuthContext`.
- Error text comes from FastAPI’s `detail` (string, or the first Pydantic `msg`).

Vite proxy (`frontend/vite.config.ts`):

- `/api/v1...` is forwarded unchanged to `:8000`.
- Any other `/api...` has the `/api` prefix stripped, so `/api/health` becomes `/health`.

### Forms and validation (client)

| Form | Rules in the browser | Rules the API enforces |
|---|---|---|
| Register | `required`, `type=email`, `minLength={8}` | Email format; password 8–72 characters; email stored lowercased |
| Login | `required`, `type=email` | Generic “Invalid email or password.” Inactive users get the same message. |
| Company create | `legal_name` required in the form | `legal_name` 1–255; ticker `^[A-Za-z0-9.\-]{1,32}$`; country exactly 2 chars if set |
| Company edit | display name, country, industry, sector | Same length rules. Ticker and legal name are **not** editable. |
| Upload | Browser allows only `.pdf` / `application/pdf`. Company optional. `document_type` is hardcoded to `10-K`. | Server accepts pdf/html/text via loaders, max **25 MiB**, SHA-256 dedup per user+company. Empty file rejected. |
| Ask | Submit disabled when the box is blank | Question 1–4000 characters on `/api/v1/rag/query` |

There is no shared form library. Validation is HTML attributes plus a few `if` checks.

### Styling

Tailwind CSS 4 via `@tailwindcss/vite`. Design tokens (Material-like palette, type scale, shadows) live in `frontend/src/app/styles/index.css` under `@theme`. Icons are `lucide-react`. Layout is responsive (`sm:`, `lg:` breakpoints, a collapsible sidebar pattern on the dashboard).

### Authentication behavior the UI depends on

1. `POST /api/v1/auth/register` → `201` and `{ id, email, is_active }`. The UI then calls login.
2. `POST /api/v1/auth/login` → `{ access_token, token_type: "bearer", user }`.
3. Token stored in localStorage. There is no refresh token and no `/me` endpoint. After reload, the stored user object is trusted until a later 401.
4. Logout is local only. The JWT stays valid until `ACCESS_TOKEN_EXPIRE_MINUTES` (default 30).

---

## 3. Backend (Python)

### Layering (product API)

```
routes (app/api/v1/routes)     HTTP only
  -> services                  rules, hashing, ingestion dispatch
    -> repositories            queries, flush, no commit
      -> SQLAlchemy models
        -> MySQL / SQLite
```

`get_db` in `app/db/session.py` is the transaction boundary: commit on success, rollback on exception. Exception: `IngestionService.mark_queued` calls `session.commit()` itself before the background task starts, so the “queued” status is visible immediately.

Domain errors map in `app/main.py`:

| Exception | HTTP | Body |
|---|---|---|
| `NotFoundError` | 404 | `{ "detail": "...", "error": "NotFoundError" }` |
| `ConflictError` | 409 | `{ "detail": "...", "error": "ConflictError" }` |
| `ValidationError` | 422 | `{ "detail": "...", "error": "ValidationError" }` |
| `AuthenticationError` | 401 | `{ "detail": "Invalid email or password.", "error": "AuthenticationError" }` |
| FastAPI/Pydantic request errors | 422 | `{ "detail": [ { "msg", "loc", "type" } ] }` |
| Missing/invalid JWT | 401 | `{ "detail": "Missing authentication token" \| "Invalid authentication token" \| ... }` |
| Auth secret missing at token issue/verify | 503 or 422 | “Authentication is not configured” |

CORS allows GET, POST, PATCH, DELETE. Origins come from `BACKEND_CORS_ORIGINS`.

### Authentication and authorization

- Passwords: bcrypt, rounds from `AUTH_BCRYPT_ROUNDS` (default 12). Rejected if UTF-8 length is over 72 bytes (bcrypt’s real limit). Hashes are standard `$2b$` strings.
- JWT: PyJWT, `HS256`, `sub` = user id **as a string**, `exp` = now + 30 minutes. Secret: `AUTH_SECRET_KEY`.
- `get_current_user` (`app/security.py`) requires `Authorization: Bearer`, loads the user, rejects missing, inactive, or unknown users with 401.
- There are **no roles**. Authorization is row ownership: `user_id` on `users` → `companies` and `documents`. Metrics are visible only when the parent company belongs to the caller.
- Registration conflict: `409` “An account with this email already exists.” Login never says whether the email exists.

### Services (business logic to preserve)

| Service | Responsibility |
|---|---|
| `AuthService` | Normalize email, hash, issue JWT |
| `CompanyService` | Create/search/update, scoped by `user_id`, uniqueness of legal name and ticker+exchange |
| `DocumentService` | CRUD for documents, pages, sections, chunks, tables |
| `IngestionService` | Validate upload, SHA-256 dedup, store file, mark queued, background `run_ingestion` |
| `DocumentIngestionPipeline` | validate → load → ocr → clean → sections → chunk → tables → metadata → metrics → persist |
| `ChunkEmbeddingService` | Jina batches, write vectors onto `document_chunks` and into Qdrant |
| `RagAnswerService` | Embed question, search Qdrant filtered by the user’s document ids, Groq answer, or the fixed fallback sentence |
| `FinancialMetricService` | Normalize metric name, require provenance (`document_id` plus chunk and/or page) |
| `DocumentStorageService` | Keys like `companies/{id}/{hash12}/{file}`, path traversal blocked |
| `ChatService` + `app/rag/*` | Legacy dummy/Chroma/Groq path for `/query` and `/ingest` |

Ingestion vocabulary is **not** a database enum. Allowed strings are in `app/core/constants.py` (`DOCUMENT_TYPES`, `PROCESSING_STATUSES`, `SECTION_TYPES`, `CHUNK_TYPES`, `METRIC_TYPES`, fiscal quarters `Q1`–`Q4`).

Processing statuses the UI understands: `uploaded`, `queued`, `processing`, `processed`, `partially_processed`, `failed`, plus legacy `pending` and `completed`.

RAG fallback, returned as a normal 200 when retrieval or the LLM fails:

`I don't have enough information in the provided documents.`

Upload limit: 25 MiB. Duplicate file for the same user and company returns the **existing** document (the route still uses HTTP 201). A document already in `processing` cannot be queued again (409).

### Background work

`POST /api/v1/documents/{id}/process` returns **202** `{ "document_id", "status": "queued" }` and runs `run_ingestion` on FastAPI `BackgroundTasks` (in-process, not a durable queue). A process restart drops in-flight work. The UI polls status.

### External integrations

| System | Where | Notes for .NET |
|---|---|---|
| MySQL 8.4 | `DATABASE_URL`, driver PyMySQL | Keep the database. |
| Qdrant | `app/services/vector_store_service.py` | Official .NET client exists. Collection name `document_chunks`, dimension 768. |
| Jina embeddings | `POST https://api.jina.ai/v1/embeddings`, model `jina-embeddings-v2-base-en` | Plain HTTP. Batch size 32. |
| Groq | `langchain_groq.ChatGroq`, model `LLM_MODEL` default `openai/gpt-oss-120b`, temperature 0.1, max tokens 1024 | Call Groq’s OpenAI-compatible HTTP API. Do not port LangChain. |
| Local OCR | `app/ingestion/ocr/local.py` | Needs a file-level read before choosing a .NET OCR library. Behavior must be matched with sample PDFs, not guessed. |
| PDF / HTML / text loaders | `app/ingestion/loaders` | pypdf today. .NET equivalent chosen in the ingestion module, after tests exist. |

### Configuration (do not commit secrets)

From `.env.example`: `MYSQL_*`, `DATABASE_URL`, `GROQ_API_KEY`, `JINA_API_KEY`, `EMBEDDING_*`, `QDRANT_*`, `CHAT_MODE`, `STORAGE_*`, `BACKEND_CORS_ORIGINS`, `AUTH_SECRET_KEY`, `AUTH_ALGORITHM`, `ACCESS_TOKEN_EXPIRE_MINUTES`, `AUTH_BCRYPT_ROUNDS`.

### Tests that define expected behavior

`backend/tests/` (pytest, SQLite, Alembic-applied schema): auth register/login, passwords, user ownership, repositories, services, upload, process/status, document list, stats, RAG service, chunking, embeddings, vector store, API v1. These are the specification for the C# port. They should be re-expressed as .NET tests module by module, not rewritten from memory.

---

## 4. API inventory

Snake_case JSON is the contract. ASP.NET Core’s default camelCase **must be turned off** (or a snake_case policy set) or the current UI, and any client we preserve, will break.

### 4.1 Live product API (`/api/v1`, JWT except auth)

| Method | Path | Success | Used by React today |
|---|---|---|---|
| POST | `/api/v1/auth/register` | 201 `UserRead` | Yes |
| POST | `/api/v1/auth/login` | 200 `LoginResponse` | Yes |
| GET | `/api/v1/companies?q&limit&offset` | 200 `{ items, total }` | Yes (`limit=100`, optional `q`) |
| POST | `/api/v1/companies` | 201 `CompanyRead` | Yes |
| GET | `/api/v1/companies/{id}` | 200 | No (keep) |
| PATCH | `/api/v1/companies/{id}` | 200 | Yes |
| GET | `/api/v1/companies/{id}/documents` | 200 `DocumentRead[]` | No (keep) |
| POST | `/api/v1/companies/{id}/metrics` | 201 | No (keep) |
| GET | `/api/v1/companies/{id}/metrics` | 200 | No (keep) |
| GET | `/api/v1/companies/{id}/metrics/{metric_id}` | 200 | No (keep) |
| GET | `/api/v1/documents?limit` | 200 `DocumentListRead[]` | Yes |
| POST | `/api/v1/documents` | 201 | No (keep) |
| POST | `/api/v1/documents/upload` | 201 multipart | Yes. Fields: `file`, `company_id`, `document_type`, optional `source_url`, `source_name` |
| GET | `/api/v1/documents/{id}` | 200 | No (keep) |
| POST | `/api/v1/documents/{id}/process?force` | 202 `{ document_id, status }` | Yes |
| GET | `/api/v1/documents/{id}/status` | 200 status object (includes timestamps the UI type ignores) | Yes |
| GET/POST | `/api/v1/documents/{id}/pages` | 200 / 201 | GET yes, POST no |
| GET/POST | `/api/v1/documents/{id}/sections` | 200 / 201 | No (keep) |
| GET/POST | `/api/v1/documents/{id}/chunks` | 200 / 201 | No (keep) |
| GET/POST | `/api/v1/documents/{id}/tables` | 200 / 201 | No (keep) |
| GET | `/api/v1/documents/{id}/tables/{table_id}` | 200 | No (keep) |
| POST | `/api/v1/rag/query` | 200 `{ answer, sources[] }` | Yes. Body `{ question }` |
| GET | `/api/v1/stats/dashboard` | 200 `{ documents, companies, financial_metrics }` | Yes |
| GET | `/api/v1/ingestion/stats` | 200 in-memory counters | No (keep) |

`/stats/dashboard` is also mounted **without** the `/api/v1` prefix (`app/api/router.py`) so the Vite rewrite can reach it. The live UI uses the versioned path.

### 4.2 Legacy unauthenticated API (root)

| Method | Path | Notes |
|---|---|---|
| GET | `/health/live` | `{ "status": "alive" }` |
| GET | `/health` | Pipeline flags, no secrets. **Called by the live UI.** |
| POST | `/query` | Legacy chat. Question max **500** chars (v1 allows 4000). |
| POST | `/ingest` | Legacy Chroma ingest of file paths. |
| GET | `/documents` | Legacy vector-store stats. **Different shape** from `GET /api/v1/documents`. |
| DELETE | `/reset` | Wipes the legacy vector index. Dangerous. |

### Contract details worth preserving

- Datetimes are ISO-8601 from Pydantic.
- `token_type` is the string `bearer`.
- Company list is wrapped; document list is a bare array.
- Metric money is `Numeric(30, 10)` — serialize as a JSON number or a decimal string consistently with the current API. This needs a captured sample before coding, because Pydantic’s default for `Decimal` can be a string.
- Duplicate company / email conflicts are 409 with `detail`, not 400.

---

## 5. Database

**Type:** MySQL 8.4 in Compose (`llm_db`). Local and tests fall back to SQLite. PostgreSQL is mentioned in comments and is not the running database. **Schema changes are out of scope** until you approve a specific migration.

**Ownership:** Alembic revisions in `backend/alembic/versions/`:

1. `e2a671f0f512` initial data layer
2. `d4e8a1b72c90` users
3. `a1b2c3d4e5f6` user ownership (`user_id` on companies and documents)
4. `b3f8c2a91d47` ingestion bookkeeping
5. `c5d9f2e83a10` chunk embedding columns
6. `f6c1a9e4b820` page text widened to MySQL `MEDIUMTEXT`

### Tables (9)

```
users
  └── companies (user_id CASCADE)
        └── documents (user_id CASCADE, company_id CASCADE, company optional)
              ├── document_pages
              ├── document_sections (self FK parent_section_id CASCADE)
              ├── document_chunks (section_id SET NULL)
              │     ├── financial_metrics.source_chunk_id SET NULL
              │     └── financial_tables.source_chunk_id SET NULL
              ├── financial_metrics (also FK company + document CASCADE)
              └── financial_tables
                    └── financial_table_rows
```

| Table | PK | Important columns | Constraints and indexes |
|---|---|---|---|
| `users` | int | `email`, `hashed_password`, `is_active` | Unique functional index `lower(email)`; `ix_users_email` |
| `companies` | int | `user_id`, `legal_name`, `display_name`, `ticker`, `exchange`, `country` (2), `industry`, `sector` | Unique `(user_id, ticker, exchange)` where ticker is not null (MySQL stores this as a plain unique index, so multiple NULLs are allowed); unique `(user_id, lower(legal_name))`; indexes on ticker and `lower(display_name)` |
| `documents` | int | type, title, filing dates, fiscal year/quarter, currency, `file_path`, `file_hash`, `page_count`, `processing_status`, processing timestamps and error | Unique `(user_id, company_id, file_hash)` when hash is not null; check that period end ≥ start |
| `document_pages` | bigint | `page_number`, `raw_text`, `cleaned_text` (`MEDIUMTEXT` on MySQL), `extraction_method`, `extraction_metadata` JSON | Unique `(document_id, page_number)`; `page_number >= 1` |
| `document_sections` | bigint | parent, title, type, page span, text | Check page order |
| `document_chunks` | bigint | `chunk_index`, `chunk_type`, text, token count, page span, `metadata` JSON, `embedding_vector` JSON, `embedding_id`, `embedding_model`, `embedded_at` | Unique `(document_id, chunk_index)`; `chunk_index >= 0` |
| `financial_metrics` | bigint | name, normalized name, `value Numeric(30,10)`, unit, currency, period, fiscal year/quarter, type, source page/chunk, confidence | Indexes for company+metric+period and company+fiscal; confidence 0–1; period order |
| `financial_tables` | bigint | headers JSON, units, currency, confidence, page | Confidence 0–1 |
| `financial_table_rows` | bigint | `row_index`, `row_label`, `cells` JSON | Unique `(table_id, row_index)` |

`created_at` / `updated_at` are timezone-aware datetimes, `server_default=now()`. High-volume tables use bigint primary keys. JSON is MySQL JSON (PostgreSQL JSONB only if that dialect were used). There are no database ENUM types.

Vectors in `document_chunks.embedding_vector` are a JSON array. Similarity search uses **Qdrant**, not SQL. EF Core will read and write that column; it will not be the search engine.

Transactions: one SQLAlchemy session per HTTP request. The ingestion background task opens its **own** session. That split must be kept, or a queued job will try to use a request scope that has already ended.

---

## 6. Target architecture

```
frontend-angular/          new Angular app (does not replace frontend/)
backend-dotnet/            new ASP.NET Core solution (does not replace backend/)

FinQuery.Api               controllers, middleware, Swagger, auth, JSON options
FinQuery.Application       service interfaces + implementations, DTOs, validators
FinQuery.Domain            entities matching tables, domain errors, constant sets
FinQuery.Infrastructure    EF Core DbContext, repositories, Qdrant, Jina, Groq, files
```

Cross-cutting, added once in the first backend module and reused:

- JWT Bearer authentication with the **same** secret, algorithm, `sub`, and lifetime, so existing tokens and password hashes keep working.
- Authorization = “must be this user id”, checked in services the way repositories filter today. No role system will be invented.
- Central exception middleware that emits the FastAPI-shaped body (`detail`, and `error` when the Python API sends it).
- Request logging (method, path, status, elapsed milliseconds). No bodies, no tokens, no passwords.
- Options classes bound from the same environment variable names.
- Swagger in Development.
- CORS from `BACKEND_CORS_ORIGINS`.
- `System.Text.Json` with **snake_case** property names and no surprise null-dropping on response fields the UI reads.

Angular, when approved:

- Standalone components, `provideRouter`, `provideHttpClient` with an auth interceptor.
- Functional `authGuard`.
- One `AuthService` holding the session in a signal, persisted under the same localStorage key if we want existing browsers to stay signed in during a cutover (`financeiq_auth`).
- Reactive forms for auth, companies, upload, and ask.
- Tailwind kept, with the same `@theme` tokens copied into the Angular global stylesheet, so the UI is not redesigned.
- Router URLs (`/dashboard`, `/documents`, `/companies`, `/chat`, `/settings`) so refresh keeps the page. That is a small behavior change versus React’s in-memory tab. It does not change workflows. Call this out if you want pixel-identical “refresh goes home” behavior instead.

---

## 7. Entity Framework Core — decision for approval

**Use EF Core. Do not let it own the schema.**

Why it fits:

- The Python app is already an ORM (SQLAlchemy) plus repositories. The nine tables map to entities.
- Queries are ordinary filters, joins, and counts. Nothing requires hand-written SQL as the primary style.
- LINQ can express the dashboard counts and the company search.

Why EF migrations must stay off:

- The schema has functional unique indexes (`lower(email)`, `lower(legal_name)`, `lower(display_name)`), MySQL `MEDIUMTEXT`, checks, and partial-unique indexes that MySQL already stored as normal unique indexes. EF’s MySQL providers do not round-trip that cleanly.
- Alembic is the history of this database. A second migration system will drift.
- You asked that the schema not change without approval.

How it will be configured:

- Provider: Pomelo `Pomelo.EntityFrameworkCore.MySql` (the maintained MySQL provider for EF Core).
- Connection string translated from `DATABASE_URL` / `MYSQL_*`. The Python URL (`mysql+pymysql://...`) is not an ADO.NET string; the .NET app will read the same host, database, user, and password.
- `DbContext` fluent config names every table and column explicitly, including `document_chunks.metadata` and `document_pages.extraction_metadata`.
- JSON columns mapped with value converters. Embedding vectors stored as JSON text/arrays, not as a SQL vector type.
- `Database.Migrate()` and `EnsureCreated()` will not be called.
- Uniqueness that the functional indexes enforce (case-insensitive email and legal name) stays in the service, matching `AuthService` and `CompanyService`. The database remains the backstop.
- SQLite remains a test option only if the EF model can be applied to a throwaway SQLite file **without** altering MySQL. If that proves awkward, tests will use Testcontainers MySQL. That choice is made when the first repository tests are written, not by changing production.

**Not using Dapper as the primary tool.** A few queries can still be raw SQL later if a translation fails. Starting with Dapper would throw away a mapping that EF can do and would not avoid the “do not change the schema” rule.

**Qdrant, Jina, Groq, and the file store stay outside EF.** They are infrastructure services behind interfaces.

---

## 8. File mapping

Existing files stay where they are. This is the intended new layout.

| Current | New |
|---|---|
| `backend/app/main.py` | `backend-dotnet/src/FinQuery.Api/Program.cs` |
| `backend/app/api/v1/routes/*.py` | `FinQuery.Api/Controllers/*Controller.cs` |
| `backend/app/api/routes/health.py` | `HealthController` (unversioned) |
| `backend/app/api/routes/{query,ingest,documents}.py` | Deferred. See risks. Not required for the live UI. |
| `backend/app/services/*.py` | `FinQuery.Application/Services` |
| `backend/app/repositories/*.py` | `FinQuery.Infrastructure/Persistence` |
| `backend/app/models/*.py` | `FinQuery.Domain/Entities` |
| `backend/app/schemas/*.py` | `FinQuery.Application/Dtos` |
| `backend/app/security.py`, `app/auth/*` | `FinQuery.Infrastructure/Auth` + JWT middleware |
| `backend/app/core/errors.py` | `FinQuery.Domain/Errors` + exception middleware |
| `backend/app/core/config.py`, `app/auth/config.py` | `IOptions` sections |
| `backend/app/ingestion/**` | `FinQuery.Infrastructure/Ingestion` after CRUD is proven |
| `backend/app/services/vector_store_service.py` | `FinQuery.Infrastructure/VectorStore` |
| `backend/app/rag/**`, `legacy/streamlit_app.py` | Not ported unless you ask. Superseded by the v1 RAG path. |
| `frontend/src/services/api.ts`, `types/api.ts` | `frontend-angular/src/app/core/api` |
| `frontend/src/auth/*` | `core/auth` service, guard, interceptor |
| `frontend/src/features/**` | `features/**` standalone components |
| `frontend/src/app/styles/index.css` | Angular global styles (tokens copied) |

---

## 9. React → Angular mapping (when Phase 3 is approved)

| React | Angular |
|---|---|
| Function components | Standalone components |
| `useState` for screen state | Signals (`signal`, `computed`) |
| `useEffect` data loads | Service calls in the constructor or `toSignal` / explicit load methods. Destroyed subscriptions via `takeUntilDestroyed`. |
| `AuthContext` | `AuthService` + `authGuard` |
| `fetch` in `api.ts` | `HttpClient` + interceptor that sets Bearer and, on 401, clears the session |
| Manual forms | Reactive forms. Validators mirror the current client rules; the API remains the source of truth. |
| In-memory nav id | Router paths with the same five destinations |
| Tailwind classes | Same classes, Tailwind available to the Angular build |
| `lucide-react` | `lucide-angular` (same icon names) |
| Unused `ChatPage`, `HomePage`, `StatusRow` | Not migrated. They are not in the running UI. |

---

## 10. Risks

| Risk | Why it matters | Mitigation |
|---|---|---|
| Ingestion parity | The pipeline (cleaners, section patterns, metric regexes, table splitting, OCR) is large custom Python. A “clean” C# rewrite will change answers. | Port only after golden tests: same PDF in, compare pages, sections, chunks, metrics, and status. Do this as its own module, after auth and CRUD. |
| Two RAG stacks | Porting both Chroma and Qdrant doubles scope and the live UI uses only Qdrant. | Port `/api/v1/rag/query` only, unless you explicitly want `/query`, `/ingest`, and `/reset`. |
| Password and JWT compatibility | Existing users must log in. | Verify a known bcrypt hash with BCrypt.Net. Issue HS256 JWTs with string `sub` and the same secret. Add a test that a Python-issued token is accepted and the reverse. |
| JSON naming and error shape | The UI reads `access_token`, `legal_name`, `detail`. | Snake_case serializer. Exception middleware returns `detail`. Contract test one response per endpoint against a recorded Python sample. |
| Decimal and datetime serialization | Metric `value` and timestamps can drift (string vs number, `Z` vs offset). | Capture one real response before writing the DTO serializer. |
| Functional unique indexes | EF will not generate `lower(email)`. | Services keep the case-folding. EF does not migrate. |
| Background ingestion | `Task.Run` tied to a request scope will lose work or use a disposed `DbContext`. | A channel + `BackgroundService` with its own scope, matching `run_ingestion`. Still in-process, same limitation as FastAPI. A durable queue is a later decision, not a silent upgrade. |
| `mark_queued` commits early | If the .NET unit-of-work commits only at the end, a crash before return can leave the document unqueued, or a double-commit can surprise. | Queue status is saved before the job is enqueued. Same as today. |
| OCR and PDF libraries | Different extractors change chunk text and therefore embeddings. | Do not swap libraries casually. Diff extracted text on a fixture PDF. |
| Groq model name | Default `openai/gpt-oss-120b` must be confirmed as still valid when the module is built. | Read from configuration. Do not hardcode a new model. |
| Upload dedup returns 201 | Easy to “fix” into 200 and break clients. | Keep 201. |
| Legacy `DELETE /reset` | Unauthenticated index wipe. | Do not reimplement unless you ask. |
| Styling drift | Rewriting Tailwind into Angular Material would be a redesign. | Keep Tailwind and the token file. |
| .NET 10 / Angular version pin | “Latest” moves. | Pin exact SDK and CLI versions in the first implementation PR and record them here. |

---

## 11. Implementation sequence (after approval)

Work stops at the end of each module until you say to continue. Each module: explain the Python, list files, implement beside it, build, fix, summarize, and note the interview idea.

**Phase 2 — backend, in this order**

1. **Solution skeleton** — .NET 10 Web API, layers, Serilog or the built-in logger, exception middleware, snake_case JSON, Swagger, health `GET /health` and `GET /health/live`, CORS, configuration. No database calls yet beyond a later module.
2. **EF model + user auth** — map `users` only first, prove read/write on the existing table, register, login, JWT, bcrypt compatibility test.
3. **Companies** — list/create/get/patch with the same validation and 409 behavior.
4. **Dashboard stats** — three counts, user-scoped.
5. **Documents read model** — list and get, including `company_name`.
6. **Upload + storage** — multipart, 25 MiB, hash dedup, local keys. Processing not included yet.
7. **Pages/sections/chunks/tables/metrics HTTP** — the endpoints the UI does not call but the API exposes. Straight ports of the services.
8. **Ingestion pipeline** — the large module. Status, process (202), polling fields, then each stage behind tests.
9. **Embeddings + Qdrant + RAG query** — Jina, filtered search, Groq, fallback sentence.

Legacy `/query`, `/ingest`, `/documents`, `/reset` stay out of this sequence unless you add them.

**Phase 3 — Angular, after the API those screens need is running**

1. Workspace, Tailwind, tokens, router shell, auth guard.
2. Auth screen.
3. Dashboard (stats, recent documents, ask — ask can wait if RAG is not ready).
4. Companies.
5. Documents (upload, process, viewer).
6. Settings and health.
7. Remove any dependency on port 8000 Python. Point `apiBaseUrl` at the .NET port.

**Phase 4 — integration**

CORS, the same JWT, error interceptor, upload `FormData` field names, and a smoke pass of register → company → upload → process → ask.

**Phase 5 — verification**

`dotnet build`, `ng build`, auth compatibility tests, one CRUD flow against MySQL, ingestion status flow, RAG fallback and a successful answer when keys are present. Compare status codes and JSON to the Python API. Migration is not “done” because the projects compile.

---

## 12. Decisions needed before Phase 2

1. **Approve this plan**, including EF Core with migrations disabled.
2. **Legacy API:** confirm we do **not** port `/query`, `/ingest`, `GET /documents`, `DELETE /reset`, Chroma, or Streamlit.
3. **New folders:** `backend-dotnet/` and later `frontend-angular/`. Say if you want different names.
4. **Database to use while developing:** existing MySQL (`llm_db`) or a copy. I will not run DDL against it.
5. **Router URLs in Angular:** keep the page on refresh (recommended) or match React and always reopen Overview.

Phase 2 will not start until you approve.
