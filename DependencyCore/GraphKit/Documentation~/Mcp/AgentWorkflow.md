# Agent workflow

No graph window is required. Treat IDs, generation numbers and capability descriptions returned by tools as authoritative; do not infer project node types or fabricate endpoint keys.

## Discover and edit

1. Call `haru_graph_inspect` with `action: "search_documents"`, paging candidate files until `next_offset` is null, even when a page has no entries. Check failures separately. Search matches asset paths under Assets, not scene contents. `list_documents` pages documents in an exact asset path, including explicitly registered nested bindings; follow its next_offset too, especially when search reports documents_truncated.
2. Open the chosen document with `haru_graph_draft` action `open`, supplying both the returned asset_path and document_id. Read `status`, `nodes`, `endpoints`, root/library queries and type candidates. Action, Formula, Token, Asset and Property use their declared contracts. Ordinary open reads Owner memory; use inspect's list_windows/capture_window to inspect unsaved window drafts.
3. Query `value_fields` or `structured_fields` before writing. Metadata descriptor IDs replace reflected IDs when a binding supplies descriptors. Read-only or invisible fields cannot be bypassed through the reflected value/structured routes.
4. For `[SerializeReference]` data, query `structured_types` at the returned value_path. Choose an exact type with `replace_structured_value`; initialize ordinary records/lists with `initialize_structured_value`. A list of managed references requires a type for insertion. Disconnect embedded graph links before removing/replacing their containing record.
5. Requery generation and endpoint keys after every change. `apply_batch` groups commands within one draft and rolls back a failed batch as one Undo operation. `check` expresses query assertions; `validate` checks graph structure and document diagnostics. Neither executes the graph.

## Discover execution contracts

Call `haru_graph_execute.providers` with the draft ID and generation, then `describe` with an exact provider_id. The provider must describe input, output, supported graph semantics and isolation responsibilities. An empty provider list means execution is unavailable for this consumer; do not substitute an arbitrary reflection runner.

Call `run` with a provider-defined input object and optional cooperative timeout. The graph and endpoint maps are cloned together; the original draft, generation and preview remain unchanged. Unity object references are shared unless the provider explicitly copies them. Providers own world/dependency isolation and cleanup. Outer success means the provider returned a result; evaluate the provider's own pass/fail assertions in that result.

## Save one or several files

For an independent file use `preview` then `commit`. For a new Owner/asset use `preview_save_as` then `save_as`. Inspect before/after snapshots and token/generation before writing. A shared asset with dependents remains blocked in the single-file path.

Save As leaves the original draft targeting its original source. Open the returned asset_path/document_id to verify or continue editing the new asset. A normal single-file commit retains its draft; a successful multi-document transaction consumes its drafts. Check nested command results, validation passed/errors and provider assertions; a successful transport batch is not proof that its contents passed.

For several documents or a shared asset:

1. Call `haru_graph_transaction.dependencies` with `drafts:[{draft_id,generation}]` for the intended changes.
2. Open every document listed for the affected files, including unchanged dependents. Do not discard an existing working draft to replace it with a fresh automatic one. Unsupported dependency files block the transaction.
3. Inspect each member's working copy and validate the intended end state. Submit all members to `preview`. The preview includes file/document closure and before/after hashes; the member captures/history provide content review. Up to 16 persistent drafts can participate.
4. Pass the single-use preview_token to `commit`. It rechecks conflict guards and dependency closure, stages all writes, invalidates validation flags, validates the staged references and saves selected files. A validation error stops the group and triggers rollback.
5. On success, drafts are consumed: reopen saved documents and verify the resulting contents. On failure, inspect Diagnostic, RollbackSucceeded and RecoveryDirectory. After a write attempt the old sessions require reopening; do not blindly retry.

The transaction has compensating file/memory rollback, not crash-atomic persistence. Recovery backups are retained under `Library/GraphKitAssetRecovery`. After interrupted persistence, inspect `paths.txt`, `state.txt`, the numbered `.before` files and current assets before choosing recovery. Never overwrite subsequent user edits with an old backup.

## Consumer integration responsibilities

Register stable document bindings using `HaruGraphExtensions.RegisterDocuments` and executors using `RegisterExecutor`. Retain/dispose registration leases and register again after domain reload. Supply the existing GraphKit context for custom root adapters, metadata and diagnostics. GraphKit and GraphMcp must not reference a consuming game's types. Keep provider-specific implementations in the consumer's Editor assembly.

## Flow and layout tasks

Prefer `flow` and `flow_candidates` over guessing node coordinates or concrete types. Follow every next_offset and inspect complete/issues. Use structural commands for insertion, removal, contiguous transfer and wrapping; set the new container's inputs explicitly through existing endpoint/value commands. Requery paths after mutations, including inside batches via fresh queries and $ref. A shared node edit affects all uses; never duplicate sources, add state or change evaluation timing just to reduce wires.

Before a layout-only task, inspect the current Owner and any unsaved window draft; open a draft from the intended source. Call protect with layout_only:true and the requested protected group/node IDs before edits. Whole-group protection includes all tabs and dependency closures; do not promise one-tab granularity. Policy is fixed until discard, and Undo/Redo are unavailable while protected. Use bounded batches and inspect rollback errors.

Use group_scopes/groups/group_tabs, then geometry or layout_preview with scope_id/group_id/tab_index. These background queries do not control the user's window. Measure bounds/ports before placement; use overlap data and anchor heights, not a fixed grid alone. Inspect boundary counts and the schematic limitations. Do not focus, maximize, activate visible tabs or send window events to obtain a picture. No screenshot or measured crossing/text-clipping evidence means visual acceptance remains pending.

Before persistence require layout_diff.content_unchanged for a pure layout task and protection_passed for protected work; verify node comparison completeness separately from full hashes. Validation-state invalidation is normalized on both content comparisons; other content/reference changes remain failures. Then follow the existing preview/commit or dependency transaction, reopen, and reread protected/current content. Report structural, disk and visual evidence separately.

## Per-node composition and connection modes

Read `nodes` or source entries in `endpoints`: `node_mode` is `Composition` or `Connection`, and `read_user_count` counts distinct read Slots (excluding Property write targets and duplicate Union wrappers). New carriers default to Composition. Dragging a Node into a Slot/list selects Composition; dragging a Port wire selects Connection automatically, preserving the same carrier and allowing one-to-many reads. The UI has no mode selection menu. Dragging a nested Node onto empty canvas preserves its references and selects Connection.

MCP `connect` represents Port wiring: it selects Connection in the same atomic edit, including when reconnecting the same source. Property write-target selection does not change the reader carrier's mode. `check_connection` checks actual compatibility without changing mode. Requery generation and endpoints after every successful edit. Connection stays external even with one reader; it never becomes Composition merely because other readers disconnect.

For background composition authoring, use `configure_node` with the current draft/generation, exact node_id and node_mode:"Composition". Disconnect extra read Slots first; multiple readers reject the change without automatic copying/disconnection. Mode changes are content edits and obey normal Undo/Redo, atomic batches, protection and persistence guards. Optional position places a standalone source. Mode does not override type, cycle, group/tab or nesting constraints.
