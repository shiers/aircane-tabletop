# RAG System and Rules Lookup: Current State

_Last updated: 2026-10-09_

This document describes how Aircane turns imported rulebooks into searchable context, and how Rules Lookup and the AI DM use that context to answer questions. It covers the current implementation, recent fixes, and known gaps.

## Overview

```
 PDF upload / watched folder / built-in bundle
            │
            ▼
 DocumentImportJob ── text extraction (PdfPig) ── OCR if needed (Tesseract)
            │
            ▼
 SlidingWindowTextChunker (2000 chars, 200 overlap, per page)
            │
            ▼
 IEmbeddingProvider ──► DocumentChunks (text + vector(768) + provenance)
                                     │
 Rules Lookup / AI DM player action  │
            │                        │
            ▼                        ▼
 RagContextBuilder ──► RetrievalService (keyword + vector, filtered)
            │
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
| Context assembly | `src/backend/Aircane.Infrastructure/Retrieval/RagContextBuilder.cs` |
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
3. **JSON:** marked `Completed` with no text extraction, so JSON documents are not searchable.
4. **Other extensions:** marked `Failed`.
5. Each page is chunked by `SlidingWindowTextChunker`:
   - Chunks are at most 2000 characters, with 200 characters of overlap.
   - It prefers to split at a newline or period within the last 200 characters of a chunk.
   - Every chunk keeps its page number and inherits the document's visibility.
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
- **Optional filters:** `SourceType` and `Ruleset` (exact match).
- **Game system:** expanded via `IGameSystemCanonicalizer.GetEquivalentNamesAsync` to the definition's name, its identifier and all its aliases. A document matches if its stored game system is any of those. This means canonicalized uploads and alias-tagged built-ins (`D&D 5e`) are both found by one filter. If no definition matches, the filter is an exact match on the given value.

### Keyword search
- `KeywordQueryParser` turns the question into terms:
  - Lowercases it and splits on anything that isn't a letter or digit.
  - Drops English stopwords and question words ("explain", "how", "rule", …), plus tokens under 3 characters.
  - Removes duplicates and keeps at most 8 terms.
  - If nothing survives (e.g. "how does it work"), the whole question is used as one phrase.
- A chunk matches if its text or section title contains **any** term. Matching is case-insensitive and substring-based (`lower(...) LIKE '%term%'`).
- Results are ranked **in the database** by how many terms they contain, then `TopK` is taken.
- Score = terms matched ÷ total terms.

### Vector search
- Embeds the question and orders chunks by cosine distance; score = 1 − distance.
- Only chunks whose provenance matches the active provider are used. Chunks with no provenance recorded are treated as compatible.
- Mismatched chunks are counted and logged as a "re-index required" warning.
- Returns nothing on the InMemory provider (tests) or when vector search is disabled.

### Merge
| Found by | Score |
|---|---|
| Keyword only | `0.5 × fraction of terms matched` (0.5 for a full match) |
| Vector only | the cosine similarity |
| Both | `min(1, keyword + 0.5 × vector)` |

Results are sorted by score, then `TopK` is taken.

## 3. Context assembly (`RagContextBuilder`)

1. Sets the maximum visibility from the caller's role:
   - `Host` and `HumanDm` get `DMOnly`.
   - `Player`, `Spectator` and anything else get `Public`.
2. Calls `SearchAsync`.
3. Drops chunks scoring below `MinRelevanceScore`. Chunks with a null score are kept.
4. Source-priority ordering (Homebrew > Rules > Adventure > …) is **defined but not applied**. `ApplySourcePriority` returns the input unchanged, because `ChunkResultDto` doesn't carry the source type.
5. Formats the chunks as `[N] (Title - Section, p.X)` followed by the text, until `MaxContextChars` is reached. It then appends a `Sources:` list if it fits.

## 4. Rules Lookup

### Request flow
- **UI:** `RulesLookupView.vue` sends `POST /api/ai/rules-question` with `{ question, gameSystem, ruleset }`.
  - The game system dropdown is populated from the active Game System Definitions (definition name as value), plus "Any game system".
  - The ruleset is free text.
- **Endpoint:** `RulesQuestionController` requires an authenticated user.
- **`RulesQuestionService.AskAsync`:**
  - Calls `RagContextBuilder` with `TopK: 20`, `MaxContextChars: 8000`, `MinRelevanceScore: 0.3` and `RequesterRole: Host`.
  - `HasSourceSupport` = at least one chunk was included.
  - **With sources:** the system prompt says to answer only from the context, cite as `[N]`, separate official rules from house rules from interpretation, and not reveal DM-only info.
  - **Without sources:** the prompt says to start with a disclaimer, mark general knowledge as unverified, and suggest importing the rulebook. The UI then shows the **"Unverified Answer"** banner.
  - Citations are enriched with license metadata for built-in documents (`CitationLicenseEnricher`).

### Other consumer
`PlayerActionService` (AI DM) uses the same builder and parameters. It takes `GameSystem` and `Ruleset` from the campaign, and the player's action text is the query.

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

## 6. Known gaps and risks

| # | Issue | Impact | Notes |
|---|---|---|---|
| 1 | **Rules Lookup always retrieves at Host visibility**, but the endpoint is open to any authenticated user | Players can receive `DMOnly` chunks in answers | Only a prompt instruction guards this. The caller's real role should be passed through. |
| 2 | Source priority is not applied | House rules don't outrank core rules | Add `SourceType` to `ChunkResultDto` and use `ApplySourcePriorityWithType`. |
| 3 | Keyword match is substring-based | Noise: "actor" also matches "factors" and "contractor" | Deliberate, for OCR text with no spaces. Postgres full-text search (`tsvector`) or `pg_trgm` would rank better. |
| 4 | Keyword ranking has no term weighting | A common term ("skill") counts as much as a rare one ("actor") | IDF-style weighting or `ts_rank` would fix this. |
| 5 | `vector(768)` column is fixed | OpenAI (1536-dim) embeddings can't be stored; vector search is disabled with that provider | Needs a schema change plus re-embed. Tracked in the backlog. |
| 6 | Built-in bundle game systems aren't canonicalized | SRD stored as `D&D 5e` | Works now through alias expansion, but stored data is inconsistent. |
| 7 | Ruleset filter is exact match, free text | A typo in Ruleset returns no sources | Same failure mode as the game system bug. Consider a dropdown or normalization. |
| 8 | No retrieval integration tests against Postgres | Translation of the keyword expression and pgvector queries isn't covered by tests | Unit tests use EF InMemory, where vector search is skipped. |
| 9 | JSON documents are not chunked | Imported JSON content isn't searchable | |
| 10 | Page numbers are PDF page indexes | Citations can differ from the printed page (PHB Actor feat: PDF p.154, printed p.165) | |
| 11 | `docs/known-limitations.md` is partly stale | It says there's no cloud embedding provider and no alias table; both now exist | Update it alongside this doc. |
