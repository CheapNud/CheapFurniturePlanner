<!--
  TODO.md — CheapFurniturePlanner project work tracker
  Last updated: 2026-07-23

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

- [ ] (2026-07-05) variant code: validate option/element codes exclude '-' and ':' separators (Domain/Pricing/VariantCode.cs) [audit]
- [ ] (2026-07-05) pricing: enforce MaterialKind match between fabric color's price group and price group kind (Domain/Pricing/Engine/ResolveStage.cs) [audit]
- [ ] (2026-07-05) pricing: add subtotal-stage rounding coverage (no market/test currently enables RoundStage.Subtotal) [audit]
- [ ] (2026-07-05) pricing: consult FabricOption.Required for optional fabric options (Domain/Pricing/Engine/ResolveStage.cs) [audit]
- [ ] (2026-07-05) serialization: add property-order pin test asserting PriceBreakdown's canonical key sequence [audit]
- [ ] (2026-07-05) pricing: substitution rules only rewrite foam and misc lines; extend or document (Domain/Pricing/Engine/ResolveStage.cs) [audit]

## Future

- [ ] (2026-07-22) upstream CheapHelpers: make CheapAccountController.SignIn virtual (or add a failure-feedback redirect option) so consumers don't need a parallel sign-in action for login feedback [bug]
- [ ] (2026-07-22) upstream CheapAvaloniaBlazor: the embedded host pipeline runs antiforgery before the consumer pipeline hook (where authentication lands), deviating from the recommended middleware order; harmless today, but an authorized antiforgery-validated form would hit odd 400s — fix belongs in CheapAvaloniaBlazor, not here [audit]
- [ ] (2026-07-22) upstream CheapAvaloniaBlazor: expose a configurable listen address on the HostBuilder (localhost-only today) — the knob that enables hosting the app on a LAN ("Mode B") [user]
- [ ] (2026-07-18) multi-user "Mode B": host the same app on a LAN box (single Kestrel writer, clients via browser); EF provider swap SQLite → Azure SQL / MSSQL / PostgreSQL when concurrency requires; then pay the single-user IOUs (order/service-ticket-number max+1 generation, trip numbers share the scheme, authoring doc-store load-modify-save windows, DiscountRules unique index, user-admin transactions: last-admin guard TOCTOU + non-atomic user+role insert, unit depart-vs-cancel interleave lacks a concurrency token). P2P/offline-sync deliberately ruled out (unmergeable ERP invariants). [user]
- [ ] (2026-07-13) article bridge: config→article lookup uses variant-code string equality; if one article must cover several configs, migrate to explicit value-set matching [user]
- [ ] (2026-07-13) order entry: cross-catalogue orders (an order locks to one catalogue on first item for now) [user]

## Done

_Nothing done yet._
