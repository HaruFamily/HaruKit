---
name: harugraph-mcp
description: Use HaruGraph MCP to discover, inspect, edit, test and save GraphKit documents in a connected Unity Editor. Use for graph content authoring, not arbitrary Unity scripting or developing the MCP implementation.
---

# HaruGraph MCP

Confirm the connected Unity project path and idle Edit Mode before operating. Discover the current tool schemas. No graph window is required. This skill grants no additional permission to change assets, install services, commit or publish.

The integration and this skill ship inside GraphKit. If graph tools are absent, check Window/HaruGraph/AI and MCP Setup: the optional integration requires Unity 6000.3+, MCP for Unity 10.3.x through 10.x, and Newtonsoft JSON 3.0.2 through 3.x. GraphKit works without the bridge. Do not install the retired separate GraphMcp package alongside integrated GraphKit. The setup window can export this skill; client connection settings remain managed by AgentDock or MCP for Unity.

## Discover and edit

Use `haru_graph_inspect` with action `search_documents`. Search matches asset paths under Assets; scenes and Packages are not searched. Pages count candidate files, not matched documents: follow next_offset even for empty entries. Inspect failures separately. When documents_truncated is true, use list_documents with the exact asset_path and follow all pages.

Open with `haru_graph_draft`, action open, and BOTH asset_path and document_id returned by discovery. Ordinary open reads Owner memory; inspect list_windows/capture_window when the requested subject is an unsaved window draft. Do not discard someone else's edits to resolve a conflict.

Read status, nodes, endpoints, candidates and field descriptions before changes. Treat Action, Formula, Token, Asset and Property according to their contracts. Do not guess node types, field IDs, endpoint keys or providers. Use value_fields/structured_fields before editing; managed references require structured_types and explicit replace_structured_value. Do not rewrite asset YAML or graph internals with arbitrary JSON.

Use the returned generation after each command and requery affected endpoints/paths. Transport batch success is not command success. apply_batch is an atomic group within ONE draft; a transport batch is not a transaction. Use check for expected data and validate for structural diagnostics; inspect passed/errors, not just success.

## Execute

Use `haru_graph_execute` providers, then describe for a supported provider. Submit run.input according to its contract. No provider means this consumer does not offer execution: do not claim tests passed. Outer success only means a result was returned; inspect provider-specific assertions. Execution receives a detached graph, but external Unity objects remain shared unless the provider isolates them. Cancellation is cooperative.

## Save

- Independent file: draft preview, inspect before/after snapshots and can_commit, then commit with current generation and exact preview_token.
- New file: preview_save_as then save_as with the same destination/generation/token. Save As leaves the original draft targeting the original source. Open the returned asset_path/document_id to verify or continue editing the new file.
- Multiple documents or a shared asset: haru_graph_transaction dependencies with drafts:[{draft_id,generation}]; open/include every affected document, including unchanged dependents. Unsupported files block the operation. Review each draft's capture/history and transaction preview. Commit using its preview_token: the token binds members, and a successful commit consumes their drafts.

Reopen saved documents and check the intended result. Ordinary single-file commits retain their draft; multi-document commits consume drafts. Existing clean graph windows may require reopening.

## Failures and completion

On stale generation, read current status and resolve targets again, rather than replaying old endpoints. On source/dirty-window conflicts preserve edits and resolve ownership. If a save loses its response, inspect actual files before retrying. On requires_reopen or failed persistence, inspect Diagnostic/RollbackSucceeded/RecoveryDirectory and any staging/destination paths. A failed or uncertain rollback stops writes to that group. Reopen and preview again after recovery; never overwrite later edits with an old backup. Multi-file persistence is compensating rollback, not crash atomicity.

Release only your unused drafts/snapshots; remove only temporary assets whose exact ownership is known. Preserve formal results and recovery evidence. Complete an authorized useful work segment without asking at every step.

Report the project and asset/document identities, intended changes, persistence result, structural checks, execution provider/assertions, saved-content reread, cleanup and remaining limitations. Distinguish checks run now from historical evidence. Save a usage report only when requested or needed for handoff, using the consuming project's conventions; do not append session history to this skill. Skill installation does not connect the AI client: configure MCP for Unity separately.

## Flow structure and layout

Prefer flow/flow_candidates followed by flow_insert/flow_remove/flow_transfer/flow_wrap for declared control flow. Page through flow entries; use current section and step paths and exact candidate types. Requery after every mutation, including fresh $ref queries within apply_batch. Transfer destination_index is a pre-removal gap. Set wrapper inputs explicitly. Unknown structures remain boundaries; shared nodes remain shared.

For layout-only work, set protect with layout_only:true and requested protected_node_ids/protected_group_ids before editing. Whole groups include every tab, members and dependencies. Policy is immutable for this draft; Undo/Redo are disabled while active. Do not discard/reopen merely to evade protection. Respect the latest user-defined baseline and original source/window conflict guards.

Query geometry or layout_preview with current scope_id/group_id/tab_index. These background queries do not alter a window or visible tab. Preview returns measured schematic SVG text, not a Unity screenshot; boundaries, custom controls, exact wire routes, crossings and text clipping are not fully represented. Do not create/focus/maximize a window or activate visible tabs for verification. Measure before placing, inspect overlaps and ports, and mark missing visual evidence as pending.

Require layout_diff.content_unchanged for pure layout and protection_passed for protected work before the normal preview/commit flow. Content hashes retain reference sharing and normalize declared validation invalidation; node-change entries have separate truncation/completeness flags. Reopen and reread after save. Never copy sources, add Properties/Tokens or change execution/evaluation semantics to make the picture smaller. See Documentation~/Mcp/AgentWorkflow.md and Maintenance.md for exact limits.

## Per-node composition and connection modes

Read `nodes` or source entries in `endpoints`: `node_mode` is `Composition` or `Connection`, and `read_user_count` counts distinct read Slots (excluding Property write targets and duplicate Union wrappers). New carriers default to Composition. Dragging a Node into a Slot/list selects Composition; dragging a Port wire selects Connection automatically, preserving the same carrier and allowing one-to-many reads. The UI has no mode selection menu. Dragging a nested Node onto empty canvas preserves its references and selects Connection.

MCP `connect` represents Port wiring: it selects Connection in the same atomic edit, including when reconnecting the same source. Property write-target selection does not change the reader carrier's mode. `check_connection` checks actual compatibility without changing mode. Requery generation and endpoints after every successful edit. Connection stays external even with one reader; it never becomes Composition merely because other readers disconnect.

For background composition authoring, use `configure_node` with the current draft/generation, exact node_id and node_mode:"Composition". Disconnect extra read Slots first; multiple readers reject the change without automatic copying/disconnection. Mode changes are content edits and obey normal Undo/Redo, atomic batches, protection and persistence guards. Optional position places a standalone source. Mode does not override type, cycle, group/tab or nesting constraints.
