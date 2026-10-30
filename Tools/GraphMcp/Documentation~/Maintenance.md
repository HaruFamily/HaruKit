# HaruGraph MCP integration

This optional Editor package exposes `haru_graph_inspect`, `haru_graph_draft`, `haru_graph_execute` and `haru_graph_transaction`. It owns generic graph discovery, authoring and transaction protocols; consumer code supplies execution semantics through explicit registration. See `AgentWorkflow.md` for the operating sequence.

## Installation and dependencies

The consuming Unity project must install GraphKit, MCP for Unity (`MCPForUnity.Editor`) and Newtonsoft JSON. This package follows HaruKit's explicit consumer-manifest dependency convention; it does not install or change the MCP transport automatically. Add `com.harufamily.tools.graphmcp` from the repository path `Tools/GraphMcp`. The integration assembly is `HaruFamily.Tools.GraphMcp.Editor`.

Assembly dependencies are GraphKit runtime/editor, MCP for Unity, Newtonsoft JSON and Unity/system assemblies. Do not reference LogicGraph, AssetPipeline, UniTask, Assembly-CSharp or a game's definitions. GraphKit itself must remain independent of this optional integration.

For a checkout named `HaruKit` beside the project's `Assets` directory, use these two manifest entries:

```json
"com.harufamily.dependencycore.graphkit": "file:../HaruKit/DependencyCore/GraphKit",
"com.harufamily.tools.graphmcp": "file:../HaruKit/Tools/GraphMcp"
```

Install MCP for Unity and its declared dependencies separately. Enable package tests with `com.harufamily.tools.graphmcp` (and optionally `com.harufamily.dependencycore.graphkit`) in `testables`.

## Authoring contracts

Document discovery uses direct serialized `IGraphDocument` fields, `IEditableGraphAsset`, or explicitly registered bindings; supported operations use `HGDocumentSession`, shared metadata and slot contracts. Preserve Action, Formula, Token, Asset and Property semantics. Do not introduce per-project node whitelists into discovery or editing.

Draft writes require the current generation. Keys from endpoints and root/list queries expire after mutations. `apply_batch` is atomic only within one draft, keeps one Undo entry and requires fresh query references after edits. `check` accepts only read operations. `validate` is structure/document diagnostics, not execution.

Single-file saves use preview/commit or preview_save_as/save_as. Referenced graph assets retain the single-file blocker; use `haru_graph_transaction` for a complete dependency-aware commit. Keep source-memory/disk/window conflicts and missing-reference checks. Up to 16 drafts can be open.

`RegisterDocuments` returns a disposable registration lease. Each provider supplies at most 64 uniquely keyed `HaruGraphDocumentEntry` values per Owner, with a binding and optional `HGEditorExtensionContext`. Discovery IDs combine persistent Owner identity, provider ID and entry key; bindings can address nested/multiple documents. A provider must return stable keys, read without mutation, write only its document, and retain the submitted reference. Discovery still scans ScriptableObject asset files, not scene components. Disposing a registration prevents future discovery; existing sessions retain their binding/context.

Managed-reference fields and list elements use `structured_types` followed by `replace_structured_value` (exact returned type, no value payload). Clear with `value:null` and no type. Only assignable, serializable data classes with a public default constructor are offered; graph carriers, Unity objects, delegates and document internals are excluded. Connected nested graph references must be disconnected before replacement/removal. `initialize_structured_value` initializes ordinary records/lists; managed-reference instances require explicit type selection. Paths and keys must be requeried after mutation.

Registered metadata replaces reflection for a described node. `metadata_fields`/`set_metadata_value` expose visible scalar descriptors; `value_fields`/`set_value` respect the same descriptor IDs. `structured_fields` follows descriptor IDs into composite data and preserves visibility/read-only constraints. A consumer may declare `managedReference:true` on HGFieldDescriptor.Create when its setter persists managed-reference data; the default is false and must not be inferred from the runtime type. Slot operations remain typed graph commands; JSON cannot replace arbitrary graph internals.

## Consumer execution integration

Execution contracts are registered with `RegisterExecutor(IHaruGraphExecutor)`. `haru_graph_execute` provides `providers`, `describe` and `run`; all require a current draft/generation. A provider describes its input, result and isolation contract and receives a detached snapshot, copied input and cooperative cancellation token. Inputs are bounded to 256 KiB, responses to 1 MiB, timeout to 1..30000 ms. The provider must validate inputs, honor cancellation where feasible, and clean up in finally blocks. A successful invocation is not a statement that provider-specific assertions passed: inspect its result. Duplicate IDs fail; disposal unregisters; integrations register again after domain reload. No executor is discovered by scanning arbitrary graph classes.

A consumer may also keep its separately named tool. `HaruGraphDraft.WithDraftSnapshot` checks draft identity, expiration, generation and Editor readiness, then supplies a `HaruGraphDraftReadContext` with a copied document and endpoint dictionaries copied together. Node IDs and current endpoint keys address the same copied graph. The consumer receives neither the owning session nor its commit API.

Unity object references remain shared: a detached graph is not a sandbox for external assets or world effects. Consumers must audit/copy dependencies and manage their own context, cancellation, disposal and assertions. Only explicitly registered trusted Editor providers can be selected; no process sandbox or forcible preemption is provided. The value codec (`TryValue`/`WireValue`) and persistent identity helper (`HaruGraphInspect.OwnerKey`) are reusable transport helpers, not permission to execute nodes.

## Validation

Verify the assembly reference boundary and run protocol-level transactions with multiple consumers, including a custom document. Do not infer portability from one game's execution tests. Record separately compilation in the host, consumer protocol coverage, and testing in a separate clean project.

The package includes `HaruFamily.Tools.GraphMcp.Editor.Tests` under `Tests/Editor`. Add `com.harufamily.tools.graphmcp` to the consuming manifest's `testables` to run it. These tests check the compiled integration and test assembly reference allowlist, reject consumer execution actions through the public command entry, and verify invalid snapshot requests cannot invoke a consumer. They need no game definitions, LogicGraph or AssetPipeline. The allowlist covers direct references, not a transitive dependency audit.

Consumer transaction and persistence tests remain in the host integration suite because they intentionally depend on consumer document types. Package boundary tests do not replace those workflows or constitute a clean-project installation test.

Changes here affect static Editor tools, not the serialized GraphKit carrier namespace or assembly. Preserve script `.meta` identities when moving existing files. Do not add serialized compatibility shims for the transport's former source location.

## Reproducible independent installation

The package includes a minimal custom `IGraphDocument` protocol test. It discovers a temporary Owner without a window, edits a field on a detached draft, checks stale generation rejection and Undo/Redo, validates, commits with a preview token, unloads/reimports the asset, and reopens it. It cleans its temporary assets and snapshots and requires no consumer framework.

`Validation~/prepare_clean_project.py --source-project <resolved-host> --output <new-directory>` creates an independent installation probe. The output must not exist and must be outside the host. It copies only GraphKit, GraphMcp, resolved MCP/JSON/test packages and their dependency closure; it never copies host Assets, settings (except the Editor version), or Library. Package source versions and SHA-256 digests are recorded in `installation-sources.json`. Built-in packages are supplied by the Editor. Run the printed Unity CLI test command with the installed Editor, optionally passing `-- -nographics -logFile <log-path>`.

This is a local/offline package installation check. It does not verify registry publication, Git download, a new MCP client connection, or every Unity version advertised by package metadata. Keep those claims separate from protocol entry-point tests. On 2026-10-30 this procedure was verified with Unity 6000.3.12f1; see the project handoff report for results and artifact paths.

## Multi-document and dependent-asset commits

`haru_graph_transaction.dependencies` accepts draft/generation pairs and returns the reverse dependency closure and document IDs for each affected file. Open and include every document explicitly, including unchanged dependents; unknown file formats, scenes, Packages and files without editable graph bindings block the transaction. This conservative file-level closure never silently edits arbitrary project data.

`preview` accepts 1..16 unique persistent drafts and issues a single-use, ten-minute token. Review member hashes, affected files and each draft's capture/history. `commit` rechecks generations, graph fingerprints, memory/disk revisions, dependency closure, dirty windows, editability and the shared session guards. It stages all document clones with invalidated validation state, validates the staged references, then saves only selected asset files. Same-file multiple documents are supported. New unsaved Owners use save_as first.

The GraphKit `CommitAssetsPreservingOwners` API owns the multi-file write and rollback; GraphKit does not depend on MCP. File backups (32 MiB/file, 128 MiB total), serialized Owner memory and a path/state journal are prepared before writes. Errors restore all possible files and Owners, verify restoration and report RollbackSucceeded plus RecoveryDirectory. Successful commits consume drafts; after a write attempt all sessions must be reopened. The backup directory is `Library/GraphKitAssetRecovery/<id>` and is retained for review; `paths.txt` maps ordered entries to `<index>.before`, and state.txt records prepared/saving/committed/rolled_back. Do not restore an old backup over later user edits without comparison.

This is compensating atomicity within the running Editor, not a crash-atomic filesystem/database transaction. After a process crash or failed rollback, inspect the returned/persisted recovery directory and affected assets before any further write. Binding callbacks and validators are trusted Editor code and must honor their mutation contracts.
