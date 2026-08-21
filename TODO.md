<!--
  TODO.md — CheapFurniturePlanner project work tracker
  Last updated: 2026-08-21

  RULES FOR AI AGENTS:
  - Update the "Last updated" date above whenever you modify this file
  - Items use checkbox format: - [ ] incomplete, - [x] complete
  - Never remove completed items — they serve as history. Move them to "## Done" when a category gets cluttered.
  - Each item gets ONE line. Details go in sub-bullets indented with 2 spaces.
  - Prefix each item with the date it was added: - [ ] (2026-03-17) Description
  - When completing, change to: - [x] (2026-03-17 → 2026-03-18) Description
  - Tag the SOURCE of each item at the end in brackets:
      [code-todo] = from // TODO comment in source code
      [plan] = from a plan document or planning session
      [bug] = from a bug encountered during dev/deploy
      [audit] = from a code audit or review
      [user] = explicitly requested by the user
  - For [code-todo] items, ALWAYS include file:line reference so devs can navigate directly
  - Categories: Blocking, Planned, Future, Done
  - New items go at the TOP of their category
  - Do not create separate TODO_*.md files — everything goes here
  - Keep it terse. If it needs more than 3 sub-bullets, link to a plan document.
  - Do NOT create, rename, or remove categories — the fixed set is: Blocking, Planned, Future, Done
  - When asked for planned work or TODO analysis, ALWAYS include Future items too — list them below Planned and note them as future work
-->

# TODO

## Blocking

_Nothing blocking._

## Planned

- [x] (2026-07-28 → 2026-07-31) supplies phase: drop the retired OrderLines/SupplierReports supplier-ref string columns and delete the startup absorber once every db has migrated through the address release [plan]
- [x] (2026-07-05 → 2026-08-13) variant code: validate option/element codes exclude '-' and ':' separators (Domain/Pricing/VariantCode.cs) [audit]
- [x] (2026-07-05 → 2026-08-13) pricing: enforce MaterialKind match between fabric color's price group and price group kind (Domain/Pricing/Engine/ResolveStage.cs) [audit]
- [x] (2026-07-05 → 2026-08-13) pricing: add subtotal-stage rounding coverage (no market/test currently enables RoundStage.Subtotal) [audit]
- [x] (2026-07-05 → 2026-08-13) pricing: consult FabricOption.Required for optional fabric options (Domain/Pricing/Engine/ResolveStage.cs) [audit]
- [x] (2026-07-05 → 2026-08-13) serialization: add property-order pin test asserting PriceBreakdown's canonical key sequence [audit]
- [x] (2026-07-05 → 2026-08-13) pricing: substitution rules only rewrite foam and misc lines; extend or document (Domain/Pricing/Engine/ResolveStage.cs) [audit] — documented in place, no extension without a named need

## Future

- [ ] (2026-08-21) updates: wire velopack once CheapAvaloniaBlazor ships it (currently 3.2.0, no update api): bump the package, add WithVelopackUpdates("http://192.168.1.15:3000/cheapnud/CheapFurniturePlanner") to the host builder, inject IUpdateService for a restart chip in the app bar [user]
- [ ] (2026-08-17) ui style: the UX-2 house-style canon (PageHeader/StatusChip/pa-6 Elevation=0 content cards/ProgressButton/table plumbing, enforced by UiConventionsTests) supersedes UX-1's PageTitle-only canon on every routed page; the font swap (AppDisplay=Sora, AppBody=Inter) is a drop-in revert - replace the two files under wwwroot/fonts/ only, no theme/CSS/page edit needed (see wwwroot/css/site.css's @font-face block); the Home/Planner room-dimension caption shows a `�` in place of `×` (pre-existing text-encoding artifact, not a style regression) - separate content-bug fix, not part of this phase [plan]
- [ ] (2026-08-17) materials: material order ubl export, receipt undo, time-phased mrp - deferred remainder of the reorder-point mrp phase [plan]
  - forecast's movement scan loads all consumption history unwindowed - upgrade path: windowed grouped aggregate + per-identity existence flag
  - multi-currency prices and automatic reorder are deliberate never-items, not deferred
- [ ] (2026-08-16) auth: convert page/endpoint gating from direct role attributes to named authorization policies defined once in di (bootstrap admin gate stays a role attribute); keep the constants class, keep positive membership; one dedicated pass, gating tests updated alongside [user]
- [x] (2026-08-12 → 2026-08-13) materials audit: forecast silently skips units without a pinned catalogue version where backflush throws (same seam, divergent honesty); unresolved standalone-article units appear in the purchasing feed but not the forecast's unresolved list; a supplier-linked unit marked in-house recovers via unmark (the finish error message points at receiving) [audit] — forecast now surfaces UnpinnedUnitCodes alongside UnresolvedModelCodes, page shows both plus a standalone-out-of-scope note, FinishAsync's error names unmark as the recovery
- [x] (2026-08-12 → 2026-08-17) materials: supplier price registry, lead times/moq/reorder points (the mature legacy reorder-point model is the analog), stock audit log - shipped this phase [plan]
- [x] (2026-08-10 → 2026-08-13) catalogue export audit: csv hard-rounds to 2 decimals (diverges from the engine if a market ever skips final-stage rounding); price group label can misattribute when one colour code lives in two fabric groups of an element (pre-existing resolution semantics, no publish validation); default-selection walk assumes visibility triggers precede dependents in display order (resolver's fixed-point loop is the upgrade path) [audit] — CatalogueExport documents the rounding assumption + a test pinning every fixture market enables RoundStage.Final; CataloguePublishService.PublishAsync now returns non-blocking Warnings, one fired for a colour code shared by two fabric groups; CatalogueFlattenerTests pins the current trigger-before-dependent walk behavior
- [ ] (2026-08-10) catalogue export: structured industry-XML export deferred until a named consumer states in writing which standard+version they accept (facts to honor then: prices as integers in minor units; omitted price entry = not orderable, 0 = free) [plan]
- [ ] (2026-08-10) catalogue export: EDI price-catalogue message deferred until a counterparty demands it (flat-SKU explosion is structurally wrong for the configurator; hand-roll the writer then) [plan]
- [ ] (2026-08-10) catalogue export: per-partner CSV dialects (separator/decimals/column mapping), delta exports and multilingual names deferred until a partner names them [plan]
- [x] (2026-08-08 → 2026-08-09) forge: re-enter the ANTHROPIC_API_KEY actions secret on the forge repo so the pr-review workflow can run (actions secrets are write-only and cannot be copied between repos) — entered and proven on pr 1 [user]
- [x] (2026-07-31 → 2026-08-13) purchasing polish: announcement attach does not reject already-cancelled units (inert); supplier models button skips a redundant reload; group release saves per unit (draft-only, retry-safe) — attach now rejects Cancelled units, dialog skips the reload on a rejected add/remove, PurchasingService.ReleaseUnitsAsync releases a group in one SaveChanges (retry-safe) and the page switched to it [audit]
- [x] (2026-07-29 → 2026-08-13) planning polish: multi-laggard depart error untested for the comma join; trip region select double-clear affordance; failed-delivery confirm overwrites any earlier receiving note; pool empty-state text still says arrived-only — comma join was already correct (added the missing 2+ laggard coverage); region select had a redundant "—" item duplicating Clearable (removed it); ConfirmFailedAsync now appends instead of overwriting ReviewNote; pool empty-state text was already generic (added regression coverage) [audit]
- [x] (2026-07-28 → 2026-08-13) addresses: filtered unique index on consumer delivery addresses (one default per consumer) as a db backstop; region delete has no end-to-end ui test; the shared blank-address helper is duplicated across the party dialogs; supplier addresses cannot be cleared back to none through the dialog [audit] — HD1Backstop migration adds the filtered unique index (and fixed a real ordering bug it exposed in SetDefaultDeliveryAddressAsync's swap); region delete now covered end-to-end through the page; NewBlankAddress lives on AddressEditor only; SupplierDialog gained a Clear address action wired to a new PartyService.ClearSupplierAddressAsync
- [ ] (2026-07-27) ubl export: enable strict validation (firms now carry endpoint/tax ids after the firms release; consumer-side buyer ids still missing); discounted lines will also need allowance-charge support upstream (unit price x quantity vs discounted line total) before the validator passes [plan]
- [ ] (2026-07-26) upstream CheapAvaloniaBlazor: the embedded host serves 404 for /cheap-blazor-interop.js (seen in desktop run logs); whatever interop rides that script silently degrades — static asset wiring belongs in the library, not here [bug]
- [ ] (2026-07-22) upstream CheapHelpers: make CheapAccountController.SignIn virtual (or add a failure-feedback redirect option) so consumers don't need a parallel sign-in action for login feedback [bug]
- [ ] (2026-07-22) upstream CheapAvaloniaBlazor: the embedded host pipeline runs antiforgery before the consumer pipeline hook (where authentication lands), deviating from the recommended middleware order; harmless today, but an authorized antiforgery-validated form would hit odd 400s — fix belongs in CheapAvaloniaBlazor, not here [audit]
- [x] (2026-07-22 → 2026-08-01) upstream CheapAvaloniaBlazor: expose a configurable listen address — DECIDED AGAINST: exposing the embedded host also exposes the desktop shell's interop surface (window messaging, cookie bridge, devtools, diagnostics), turning the app into remote control with extra steps; superseded by the dedicated server entry point below [user]
- [ ] (2026-07-18) multi-user "Mode B" via a dedicated server entry point: new CheapFurniturePlanner.Server project reusing the same component/service/domain assemblies with a hardened plain ASP.NET Core host (TLS, no desktop interop or devtools, forwarded headers, login rate limiting); the desktop app stays a single-user local tool; EF provider swap SQLite → Azure SQL / MSSQL / PostgreSQL when concurrency requires; then pay the single-user IOUs (order/service-ticket-number max+1 generation, trip numbers share the scheme, authoring doc-store load-modify-save windows, DiscountRules unique index, user-admin transactions: last-admin guard TOCTOU + non-atomic user+role insert, unit depart-vs-cancel interleave lacks a concurrency token, firm one-default invariant read-then-write TOCTOU, material stock upsert find-or-create race on concurrent receipts/finishes). P2P/offline-sync deliberately ruled out (unmergeable ERP invariants). [user]
- [ ] (2026-07-13) article bridge: config→article lookup uses variant-code string equality; if one article must cover several configs, migrate to explicit value-set matching [user]
- [ ] (2026-07-13) order entry: cross-catalogue orders (an order locks to one catalogue on first item for now) [user]

## Done

_Nothing done yet._
