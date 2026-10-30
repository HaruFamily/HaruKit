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
