# RAG System and Rules Lookup: Current State

_Last updated: 2026-10-10_

This document describes how Aircane turns imported rulebooks into searchable context, and how Rules Lookup and the AI DM use that context to answer questions. It covers the current implementation, recent fixes, and known gaps.

## Overview

```
 PDF upload / watched folder / built-in bundle
            │
            ▼
 DocumentImportJob ── text extraction (PdfPig) ── OCR if needed (Tesseract)
            │
            ▼
 SlidingWindowTextChunker (1100 chars, 300 overlap, heading-aware, per page)
            │
            ▼
 IEmbeddingProvider ──► DocumentChunks (text + vector(768) + provenance)
                                     │
 Rules Lookup / AI DM player action  │
            │                        │
            ▼                        ▼
 RagContextBuilder ──► RetrievalService (keyword + vector, filtered)
            │   source priority → rerank (optional) → dedup → character budget
            ▼
 Prompt with numbered context [N] ──► IAiProvider ──► answer + citations
```

| Component | File |
|---|---|
| Import pipeline | `src/backend/Aircane.Infrastructure/DocumentProcessing/DocumentImportJob.cs` |
| Chunker | `src/backend/Aircane.Infrastructure/DocumentProcessing/SlidingWindowTextChunker.cs` |
| Embedding providers | `src/backend/Aircane.Infrastructure/Embeddings/` |
| Retrieval | `src/backend/Aircane.Infrastructure/Retrieval/RetrievalService.cs` |
| Keyword term extraction | `src/backend/Aircane.Infrastructure/Retrieval/KeywordQueryParser.cs` |
| Ruleset normalization | `src/backend/Aircane.Infrastructure/Retrieval/RulesetNormalizer.cs` |
| Text normalisation (spaced letters, run-togethers) | `src/backend/Aircane.Infrastructure/DocumentProcessing/PdfPigTextExtractor.cs` |
| Context assembly | `src/backend/Aircane.Infrastructure/Retrieval/RagContextBuilder.cs` |
| Reranking providers (None / Ollama / Cohere) | `src/backend/Aircane.Infrastructure/Retrieval/Reranking/` (interface: `Aircane.Application/Retrieval/IRerankingProvider.cs`) |
| Rules Lookup service | `src/backend/Aircane.Infrastructure/Ai/RulesQuestionService.cs` |
| AI DM consumer | `src/backend/Aircane.Infrastructure/Ai/PlayerActionService.cs` |
| Game system name matching | `src/backend/Aircane.Infrastructure/GameSystems/GameSystemCanonicalizer.cs` |
| Rules Lookup UI | `src/frontend/aircane-web/src/features/ai/RulesLookupView.vue` |

## 1. Ingestion

### Sources
- **Library uploads and watched folders.** On import, the game system is canonicalized to the matching Game System Definition name (e.g. `D&D 5e` becomes `Dungeons & Dragons 5th Edition (2014)`).
- **Built-in bundles.** The D&D 5e SRD (CC BY 4.0) and the Pathfinder 2e Remaster (ORC) are seeded from `Aircane.Workers/Resources/builtin/`. Their game system comes from the bundle manifest, which is not canonicalized. For example, the SRD is stored as `D&D 5e`.

### Processing (`DocumentImportJob`)
1. Computes a content hash, then sets the status to `Processing`.
2. **PDF:** extracts text per page with PdfPig.
   - If there is too little text, the document is marked `OcrRequired`.
   - Re-OCR runs Tesseract via `POST /api/library/documents/{id}/reocr` or `reocr-all`.
   - **Text normalisation** (`PdfPigTextExtractor.NormaliseText`) repairs spacing damage in extracted text. It runs on every page of PdfPig text-layer output, and on Tesseract output, with these steps in order:
     1. **Spaced-letter collapse** (text layer only): `(?<!\w)([A-Za-z])(?: ([A-Za-z])){2,}(?!\w)` turns `A c t o r` into `Actor`. Some PDFs, such as the 2014 PHB (ABBYY FineReader 11), encode heading characters with inter-character spacing in the font metrics, which PdfPig reads back literally. That broke keyword search for `actor` and gave headings meaningless embeddings. It is skipped for Tesseract output, which is already spaced correctly. It needs 3 or more letters, so `a b` is left alone, but three genuine single-letter tokens (`x y z`) are collapsed too. Adjacent spaced-out words collapse into one run (`AbilityScores`), and step 2 restores the break.
     2. **camelCase split:** `([a-z])([A-Z])` inserts a space (`ActorSkilled` becomes `Actor Skilled`).
     3. **Sentence-end split:** `([.!?])([A-Z])` inserts a space (`feet.Actor` becomes `feet. Actor`).
   - Steps 2 and 3 now also run on the text layer, so a legitimate internal capital (`McDonald`, `iPhone`) is split too.
   - Documents already imported keep their old text until they are re-imported (or re-OCRed).
3. **JSON:** marked `Completed` with no text extraction, so JSON documents are not searchable.
4. **Other extensions:** marked `Failed`.
5. Each page is chunked by `SlidingWindowTextChunker`:
   - Chunks are at most 1100 characters, with 300 characters (about 27%) of overlap. Both are constructor parameters (`DefaultMaxChunkSize`, `DefaultOverlapSize`).
   - Smaller chunks are more topically focused, which improves keyword precision and makes each embedding represent a narrower topic.
   - The split point is searched backwards from the end of the window, in this order:
     1. **Heading** within the last 400 characters. A heading is a markdown heading (`^\s*#{1,4}\s+\S`) or a title-case line (3–60 characters, every word capitalised except short connectors such as "of", no trailing punctuation, followed by a blank line or a line starting with a lowercase letter).
     2. **Paragraph break** (blank line) within the last 300 characters.
     3. **Newline or period** within the last 200 characters.
     4. **Hard cut** at the maximum size.
   - A heading split puts the heading at the **start of the next chunk**, so the chunk holding a section's content also holds its heading. No overlap is carried back across that boundary (it would put the previous section's text in front of the heading). Every other split overlaps by 300 characters.
   - **Section title:** a chunk that starts with a heading gets that heading as its `SectionTitle` (hashes stripped). Any other chunk gets the most recent heading before it. `DocumentImportJob` passes the last title seen into the next page, so a section that continues onto a new page keeps its title. The section title is also searched by keyword retrieval and shown in citations.
   - Headings are only detected where the extracted text has line breaks. PdfPig's text layer often runs lines together, so title-case detection fires mostly on OCR output and text-like sources; built-in bundles with markdown headings benefit most.
   - Every chunk keeps its page number and inherits the document's visibility.
   - **Re-indexing:** documents imported before this change were chunked at 2000/200 without heading awareness and keep those chunks. Re-import them (or re-OCR / re-index) to benefit. Re-ingest is a manual operator step and is never triggered automatically.
6. Embeddings are generated in one batch.
   - If embedding fails, or the provider's dimension doesn't match the column, chunks are stored **without** vectors. They remain keyword-searchable, and the document is still marked `Completed`.
7. Progress is pushed to the UI over SignalR (`ImportStatusUpdated`).

### Embeddings
| Provider | Model | Dimensions | Usable with current schema |
|---|---|---|---|
| Ollama (dev default) | `nomic-embed-text` | 768 | Yes |
| Fake (tests, `appsettings.json` default) | deterministic | 768 | Yes (not semantic) |
| OpenAI | `text-embedding-3-small` | 1536 | **No**: the column is `vector(768)` |
| Azure OpenAI | configurable | configurable | Only if 768 |

- The embedding provider is configured separately from the chat provider. In dev, chat runs on OpenAI and embeddings on Ollama.
- Each chunk records which provider, model and dimension produced its vector.
- Vectors are indexed with HNSW using cosine distance (`IX_DocumentChunks_Embedding_Cosine`).
- `EmbeddingCompatibility` disables vector search, and skips embedding on import, when the provider's dimension doesn't match the column (`Embeddings:ColumnDimension`, default 768).
- After a provider or model change, re-embed with `POST /api/library/documents/reembed-all` or `/{id}/reembed`.

## 2. Retrieval (`RetrievalService`)

`SearchAsync` runs keyword search, then vector search, in sequence, because the DbContext is not thread-safe. It then merges the two result sets by chunk ID.

### Shared filters (`BuildFilteredQuery`)
- **Visibility:** limited to the levels allowed for the caller's maximum visibility (`Public` < `Revealed` < `DMOnly` < `Hidden`).
- **Disabled documents** (`IsDisabled`) are excluded.
- **Optional filters:** `SourceType` (exact match) and `Ruleset` (normalized, see below).
- **Game system:** expanded via `IGameSystemCanonicalizer.GetEquivalentNamesAsync` to the definition's name, its identifier and all its aliases. A document matches if its stored game system is any of those. This means canonicalized uploads and alias-tagged built-ins (`D&D 5e`) are both found by one filter. If no definition matches, the filter is an exact match on the given value.
- **Ruleset:** normalized by `RulesetNormalizer` (trimmed, lower-cased, whitespace collapsed) and expanded to its alias group. The document's ruleset is compared as `lower(trim(Ruleset))`, so case and padding never matter.
  - Alias groups: `2014` (`5e 2014`, `d&d 5e 2014`, `dnd 2014`, ...), `2024` (`5.5e`, `one dnd`, `d&d 2024`, ...) and `remaster` (`pf2e remaster`, `pathfinder 2e remaster`, `2023 remaster`, ...).
  - A value outside every group (e.g. `PHB`) is matched case-insensitively against the stored value.
  - A genuinely misspelled value still matches nothing, which is why the UI offers a dropdown (section 4).

### Keyword search
- `KeywordQueryParser` turns the question into terms:
  - Lowercases it and splits on anything that isn't a letter or digit.
  - Drops English stopwords and question words ("explain", "how", "rule", …), plus tokens under 3 characters.
  - Removes duplicates and keeps at most 8 terms.
  - If nothing survives (e.g. "how does it work"), the whole question is used as one phrase.
- A chunk matches if its text or section title contains **any** term. Matching is case-insensitive and substring-based (`lower(...) LIKE '%term%'`).
- Results are ranked **in the database** by how many terms they contain, then `TopK` is taken.
- Score = terms matched ÷ total terms (before the merge weighting below).

### Vector search
- Embeds the question and orders chunks by cosine distance; score = 1 − distance.
- Only chunks whose provenance matches the active provider are used. Chunks with no provenance recorded are treated as compatible.
- Mismatched chunks are counted and logged as a "re-index required" warning.
- Returns nothing on the InMemory provider (tests) or when vector search is disabled.

### Merge
Exact terminology is more reliable than semantic similarity for rules lookups ("grapple" and "grab" are close in embedding space but mechanically distinct), so keyword matches are weighted above vector-only matches.

| Found by | Score |
|---|---|
| Keyword only | `0.7 × fraction of terms matched` (max 0.70) |
| Vector only | `0.6 × cosine similarity` |
| Both | `min(1, 0.7 × keyword fraction + 0.5 × cosine similarity)` |

Results are sorted by score, then `TopK` is taken. Each result carries the document's `SourceType`.

Because of the 0.6 weight, a vector-only chunk needs a cosine similarity of about 0.67 to reach the Rules Lookup minimum score of 0.4.

## 3. Context assembly (`RagContextBuilder`)

1. Sets the maximum visibility from the caller's role:
   - `Host` and `HumanDm` get `DMOnly`.
   - `Player`, `Spectator` and anything else get `Public`.
2. Calls `SearchAsync` for `TopK` candidates, or for `RerankCandidateTopK` (25 for Rules Lookup and the AI DM) when a reranking provider is active and available, so the reranker has a wider pool.
3. Drops chunks scoring below `MinRelevanceScore`. Chunks with a null score are kept.
4. **Applies source priority.** Chunks are ordered by source type, then by score within a type: Homebrew > Rules > Adventure > Solo > Generated > Character > Unknown. House rules therefore outrank core rulebook content even when they score lower, and are the last to be cut by the character budget.
5. **Reranks (optional).** When the active reranking provider is not "None" and is available, and there is more than one chunk, the chunks are sent to `IRerankingProvider.RerankAsync` with the question. Then, in order:
   - **Source boost:** each chunk's rerank score gets an additive boost by source type: Homebrew +0.10, Rules +0.05, everything else +0.00. Chunks are ordered by the boosted score, so relevance still leads but a highly relevant Homebrew chunk outranks an equally relevant Rules chunk. Chunks the provider does not score start from 0.
   - **Truncation:** only the top `PostRerankTopN` (7 for Rules Lookup and the AI DM) are kept, before dedup.
   - With the "None" provider this whole step is skipped: no reranker call, no boost, no truncation, and the order from step 4 stands. See "Reranking" below.
6. **Removes near-duplicates** (`RemoveDuplicateChunks`), after reranking so the better-reranked copy of a duplicated passage is the one kept. Walking the chunks in priority order, a chunk is skipped when `overlap_chars / min(len_a, len_b) > 0.40` against any chunk already kept.
   - Overlap counts the 50-character windows of the shorter text that also occur in the longer one (whitespace-normalized). Texts under 50 characters count as duplicates only if wholly contained in the other.
   - Earlier chunks win, so the higher-priority copy is kept.
   - Adjacent sliding-window chunks share at most about 27% of their text (300 of 1100 characters), below the threshold. Dedup removes near-copies, such as the same passage in two documents or an overlapping re-import, not neighbouring chunks.
7. Formats the chunks as `[N] (Title - Section, p.X)` followed by the text, until `MaxContextChars` is reached. It then appends a `Sources:` list if it fits.

### Reranking

Reranking is optional and off by default (provider "None"). It re-scores the retrieved chunks against the full question with a model, which helps natural-language questions that share few keywords with the right passage.

| Provider | How it scores | Needs |
|---|---|---|
| None (default) | Keeps the input order (scores 1.0, 0.99, ...) | Nothing |
| Cohere | `POST https://api.cohere.com/v1/rerank` (`rerank-english-v3.0` by default) | `Reranking:Cohere:ApiKey` |
| Ollama | `POST /api/embed` with `[CLS] query [SEP] chunk [SEP]` per chunk; the first value of the returned vector is the score | A reranker model installed in Ollama (`Reranking:Ollama:Model`, default `bge-reranker-base`) |

- **Never fails a request.** On a 401, a 429, any other HTTP error, a network error or a bad payload, a provider logs a warning and returns the candidates in their original order. Providers are created per request from the live settings, so a change in Settings applies without a restart.
- **Settings.** `GET`/`PUT /api/ai/settings` carry a `reranking` block (`provider`, `ollama.model`, `cohere.apiKey`, `cohere.model`). The Cohere key is returned masked (last 4 characters) and a masked or empty key on save keeps the stored key. The block is saved to `ai-settings.json` with the other AI settings under `Reranking:*` keys, and is left unchanged when a request omits it.
- **Test Connection.** `POST /api/ai/settings/test-reranking-connection` makes a real call with the unsaved settings and returns `{ available, message }` ("None" is always available).
- **UI.** AI Provider Settings has a "Reranking (optional)" section: provider select, per-provider fields and a Test Connection button. It is saved by Save Configuration.
- **Ollama caveat.** As of Ollama 0.34.4, `bge-reranker-base` is not in the Ollama model library and there is no rerank endpoint. `/api/embed` returns a pooled embedding rather than a classifier score, so "first value" is only meaningful for a model converted to expose a score there. Without the model the Ollama provider reports itself unavailable and the pipeline keeps the original order. Use Cohere unless a local model has been verified to rank sensibly.
- **Candidate count.** With reranking active the pool is 25 candidates (`RerankCandidateTopK`) instead of 10, and 7 are kept afterwards (`PostRerankTopN`). The minimum score filter (0.4) still runs on the retrieval scores *before* reranking, so a passage that scores below it never reaches the reranker, however large the pool.

## 4. Rules Lookup

### Request flow
- **UI:** `RulesLookupView.vue` sends `POST /api/ai/rules-question` with `{ question, gameSystem, ruleset }`.
  - The game system dropdown is populated from the active Game System Definitions (definition name as value), plus "Any game system".
  - The ruleset dropdown (default "Any ruleset") is populated from `GET /api/ai/rules-question/rulesets?gameSystem=...`. This returns the distinct ruleset values on enabled documents, limited to the selected game system (alias-aware), and is reloaded whenever the game system changes. A selected ruleset the new game system doesn't have is cleared.
- **Endpoint:** `RulesQuestionController` requires an authenticated user and resolves the caller's role (see below).
- **`RulesQuestionService.AskAsync`:**
  - Calls `RagContextBuilder` with `TopK: 10` (`RerankCandidateTopK: 25` and `PostRerankTopN: 7` when reranking is active), `MaxContextChars: 8000`, `MinRelevanceScore: 0.4` and the caller's role as `RequesterRole`.
  - `HasSourceSupport` = at least one chunk was included.
  - **With sources:** the system prompt says to answer only from the context, cite as `[N]`, separate official rules from house rules from interpretation, and not reveal DM-only info.
  - **Without sources:** the prompt says to start with a disclaimer, mark general knowledge as unverified, and suggest importing the rulebook. The UI then shows the **"Unverified Answer"** banner.
  - Citations are enriched with license metadata for built-in documents (`CitationLicenseEnricher`).

### Caller role and visibility
The controller reads the role claim (`ClaimTypes.Role`, issued by `ParticipantTokenService`) and passes it to `AskAsync`:
- `Host` and `HumanDm` retrieve up to `DMOnly`; `Player` and `Spectator` retrieve `Public` only.
- An authenticated caller with a missing or unrecognized role is treated as `Player`.
- An unauthenticated caller can only reach the endpoint when the policies are opened up (Development, or `Aircane:TrustLocalHost` for the desktop app). That is the local user, who is treated as `Host`.
- `AskAsync` defaults to `Player`, so a caller must opt in to DM-only content.

### Other consumer
`PlayerActionService` (AI DM) uses the same builder and the same `TopK: 10` / `MinRelevanceScore: 0.4` and reranking pool settings, but with `RequesterRole: Host`, since the AI DM narrates for the whole table. It takes `GameSystem` and `Ruleset` from the campaign, and the player's action text is the query.

## 5. Recent fixes (2026-10-09)

**Symptom:** after importing the D&D 5e PHB, Rules Lookup returned "Unverified Answer" for "explain Actor skill" with game system `D&D 5e 2014`.

1. **Game system mismatch (root cause).**
   - The PHB was stored as `Dungeons & Dragons 5th Edition (2014)`, the SRD as `D&D 5e`, and the UI's hardcoded dropdown sent `D&D 5e 2014`.
   - Retrieval used exact `==` on the game system, so nothing matched.
   - **Fixed** by alias-aware filtering in retrieval and by building the dropdown from the Game System Definitions.
2. **Keyword search matched the whole question as one substring.** "explain actor skill" never appears literally in any book.
   - **Fixed** by term extraction with match-count ranking (section 2).
   - On the real PHB data, the Actor feat chunk now ranks among the top matches. The OCR text runs the heading into the body (`…10 feet.ActorSkilled at mimicry…`), which is why matching inside words is kept rather than whole-word matching.

Tests: `RetrievalServiceTests` (alias filtering, multi-term questions, partial-match scoring) and `KeywordQueryParserTests`.

**Retrieval quality changes (2026-10-10, commit `20c1536`):**

| Change | Before | After |
|---|---|---|
| Rules Lookup visibility | Always `Host`, so players could receive `DMOnly` chunks | Caller's role; players get `Public` only |
| Source priority | Defined but not applied | Applied, using `SourceType` on `ChunkResultDto` |
| Ruleset filter | Exact match on free text | Normalized and alias-aware; dropdown in the UI |
| Merge scoring | Keyword-only 0.5×, vector-only 1×, both `keyword + 0.5 × vector` | Keyword-only 0.7×, vector-only 0.6×, both `min(1, 0.7k + 0.5v)` |
| Rules Lookup / AI DM limits | `TopK` 20, min score 0.3 | `TopK` 10, min score 0.4 |
| Context duplicates | Not handled | Chunks overlapping an earlier one by more than 40% are dropped |
| OCR text | Stored as extracted | Run-together words and sentences are separated |

Integration tests: `RetrievalServiceIntegrationTests` run retrieval and `RagContextBuilder` against a real PostgreSQL with pgvector (Testcontainers, `pgvector/pgvector:pg16`, requires Docker). They cover keyword and cosine queries, provenance filtering, visibility, disabled documents, source type, game system and ruleset alias filters, merge scoring, `TopK`, dedup and source priority. With only a few rows the planner uses a sequential scan, so they verify query correctness and ordering, not use of the HNSW index.

Tests: `RetrievalServiceTests` (scoring, source type, ruleset aliases, available rulesets), `RagContextBuilderTests` (priority, dedup), `RulesQuestionControllerRoleTests`, and `OcrTextCleanupTests`.

## 6. Known gaps and risks

| # | Issue | Impact | Notes |
|---|---|---|---|
| 1 | Keyword match is substring-based | Noise: "actor" also matches "factors" and "contractor" | Deliberate, for OCR text with no spaces. The OCR cleanup, the higher minimum score and dedup reduce the impact but do not remove it. Postgres full-text search (`tsvector`) or `pg_trgm` would rank better. |
| 2 | Keyword ranking has no term weighting | A common term ("skill") counts as much as a rare one ("actor") | Ranking is by the number of terms matched. IDF-style weighting or `ts_rank` would fix this. |
| 3 | `vector(768)` column is fixed | OpenAI (1536-dim) embeddings can't be stored; vector search is disabled with that provider | Needs a schema change plus re-embed. Tracked in the backlog. |
| 4 | Built-in bundle game systems aren't canonicalized | SRD stored as `D&D 5e` | Works now through alias expansion, but stored data is inconsistent. |
| 5 | JSON documents are not chunked | Imported JSON content isn't searchable | |
| 6 | Page numbers are PDF page indexes | Citations can differ from the printed page (PHB Actor feat: PDF p.154, printed p.165) | |
| 7 | Ollama reranking has no working default model | `bge-reranker-base` is not in the Ollama library, so Ollama reranking is unavailable out of the box | Cohere works with an API key. A local option needs a model that exposes a score through `/api/embed`, or a rerank endpoint in Ollama. |
| 8 | `docs/known-limitations.md` is partly stale | It says there's no cloud embedding provider and no alias table; both now exist | Update it alongside this doc. |
