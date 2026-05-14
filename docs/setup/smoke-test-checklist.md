# Aircane Tabletop MVP — Smoke Test Checklist

This checklist verifies release readiness for the Aircane Tabletop MVP. Run through each step manually before tagging a release candidate. All steps assume the application is running locally via `docker compose up` or the production build scripts.

## Prerequisites

- [ ] Application is running (backend API responds at health endpoint)
- [ ] PostgreSQL with pgvector is accessible
- [ ] At least one AI provider is configured (API key set via environment or user secrets)
- [ ] A folder containing at least one text-readable PDF (rules or adventure) is available on the host filesystem

---

## 1. Register a Folder and Scan for Rules Documents

| # | Action | Expected Result | Pass |
|---|--------|-----------------|------|
| 1.1 | Navigate to the Library section in the UI | Library page loads with folder and document lists | [ ] |
| 1.2 | Click "Register Folder" and enter the path to a folder containing PDF rules documents | Folder appears in the registered folders list with display name and path | [ ] |
| 1.3 | Set the default source type to "Rules" and save | Folder settings show source type as Rules | [ ] |
| 1.4 | Trigger a manual scan on the registered folder | Scan completes; documents appear in the library with status "Processing" then "Completed" | [ ] |
| 1.5 | Verify documents inherit the folder's default source type | Each scanned document shows source type "Rules" | [ ] |
| 1.6 | Open a document's detail view | Metadata displays title, filename, source type, import status, and chunk count | [ ] |

---

## 2. Create Campaign

| # | Action | Expected Result | Pass |
|---|--------|-----------------|------|
| 2.1 | Navigate to Campaigns and click "Create Campaign" | Campaign creation form appears | [ ] |
| 2.2 | Enter a campaign name, select a game system (e.g., D&D 5e 2014), choose an AI role (e.g., Co-DM), and set AI authority (e.g., Ask Before Applying) | Campaign is created and appears in the campaign list | [ ] |
| 2.3 | Open the campaign detail page | Campaign shows correct name, ruleset, AI role, and authority settings | [ ] |

---

## 3. Create Character

| # | Action | Expected Result | Pass |
|---|--------|-----------------|------|
| 3.1 | Navigate to Characters and click "Create Character" | Character creation form appears with required fields | [ ] |
| 3.2 | Fill in name, class, level, ability scores, AC, HP, and speed | Character is saved successfully | [ ] |
| 3.3 | Verify the character appears in the character list | Character displays with correct name, class, and level | [ ] |
| 3.4 | Assign the character to the campaign created in step 2 | Character is associated with the campaign | [ ] |

---

## 4. Start Session

| # | Action | Expected Result | Pass |
|---|--------|-----------------|------|
| 4.1 | From the campaign page, click "Start Session" | Session is created with an invite code and join URL displayed | [ ] |
| 4.2 | Open the join URL in a second browser tab | Join screen appears requesting display name and invite code | [ ] |
| 4.3 | Enter a display name and the invite code, then submit | Join request is sent; host sees a pending participant notification | [ ] |
| 4.4 | As host, approve the participant | Player appears in the connected participants list | [ ] |
| 4.5 | As host, assign the character to the approved participant | Player's session screen shows the assigned character | [ ] |

---

## 5. Roll Dice

| # | Action | Expected Result | Pass |
|---|--------|-----------------|------|
| 5.1 | As the player, enter a dice expression (e.g., `1d20+5`) and roll | Roll result displays with individual die result, modifier, and total | [ ] |
| 5.2 | Verify the roll appears in the session roll log | Roll log shows formula, result, roller name, and timestamp | [ ] |
| 5.3 | As the player, enter a manual roll value (e.g., 18) | Manual roll is recorded with a "manual" indicator | [ ] |
| 5.4 | Verify the roll is broadcast to the host session screen | Host sees the roll in real time via SignalR | [ ] |

---

## 6. Ask Rules Question

| # | Action | Expected Result | Pass |
|---|--------|-----------------|------|
| 6.1 | Open the rules question interface | Rules question input is available | [ ] |
| 6.2 | Ask a question covered by the imported rules (e.g., "How does advantage work?") | AI returns an answer with citations referencing the imported source document | [ ] |
| 6.3 | Verify citations include source title and page/chunk reference | At least one citation is displayed with identifiable source info | [ ] |
| 6.4 | Ask a question NOT covered by any imported source (e.g., an obscure homebrew rule) | AI responds that it cannot confirm the rule from available sources | [ ] |

---

## 7. Use AI DM Proposal

| # | Action | Expected Result | Pass |
|---|--------|-----------------|------|
| 7.1 | As the player, submit an action (e.g., "I try to sneak past the guards") | AI processes the action and generates a response | [ ] |
| 7.2 | Verify the AI response includes narration | Narration text is displayed to the session | [ ] |
| 7.3 | If AI authority requires approval, verify a proposal appears in the host's approval queue | Proposed action (e.g., RequestRoll) is visible with approve/reject controls | [ ] |
| 7.4 | As host, approve the proposal | State change is applied; player sees the result (e.g., roll request prompt) | [ ] |
| 7.5 | As host, reject a subsequent proposal | Proposal is removed from the queue; no state change occurs | [ ] |

---

## 8. Generate Adventure

| # | Action | Expected Result | Pass |
|---|--------|-----------------|------|
| 8.1 | Navigate to Adventure Generation | Generation input form appears | [ ] |
| 8.2 | Fill in parameters: ruleset, party size, level range, tone, length, and difficulty | Generation starts; progress or staged output is shown | [ ] |
| 8.3 | Wait for generation to complete | Draft adventure is created with overview, scenes, NPCs, and encounters | [ ] |
| 8.4 | Review the draft in the review UI | Sections are editable; regenerate option is available per section | [ ] |
| 8.5 | Approve the adventure | Adventure status changes to "Approved" and appears in the adventure list | [ ] |
| 8.6 | Verify the adventure is indexed and searchable | Adventure chunks appear in library search results | [ ] |

---

## 9. Verify Source-Unavailable Warning

| # | Action | Expected Result | Pass |
|---|--------|-----------------|------|
| 9.1 | Rename or move the registered folder to a different path on the filesystem | Folder is no longer accessible at the original path | [ ] |
| 9.2 | Trigger a rescan on the registered folder (or wait for availability check) | Scan reports an error or marks documents as unavailable | [ ] |
| 9.3 | Check the library UI for source-unavailable warnings | Documents from the moved folder show "Source Unavailable" status | [ ] |
| 9.4 | Verify document chunks are still searchable (indexed data persists) | Rules questions still return cached chunks, but source availability warning is shown | [ ] |
| 9.5 | Restore the folder to its original path and rescan | Documents return to "Available" status; warnings are cleared | [ ] |

---

## Summary

| Section | Result |
|---------|--------|
| 1. Register Folder & Scan | [ ] Pass / [ ] Fail |
| 2. Create Campaign | [ ] Pass / [ ] Fail |
| 3. Create Character | [ ] Pass / [ ] Fail |
| 4. Start Session | [ ] Pass / [ ] Fail |
| 5. Roll Dice | [ ] Pass / [ ] Fail |
| 6. Ask Rules Question | [ ] Pass / [ ] Fail |
| 7. AI DM Proposal | [ ] Pass / [ ] Fail |
| 8. Generate Adventure | [ ] Pass / [ ] Fail |
| 9. Source-Unavailable Warning | [ ] Pass / [ ] Fail |

**Release Candidate Verdict:** [ ] Ready / [ ] Blocked

**Tester:** ___________________  
**Date:** ___________________  
**Build/Commit:** ___________________  
**Notes:**

---
