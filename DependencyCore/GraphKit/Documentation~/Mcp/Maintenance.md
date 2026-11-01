# HaruGraph MCP integration

This optional Editor package exposes `haru_graph_inspect`, `haru_graph_draft`, `haru_graph_execute` and `haru_graph_transaction`. It owns generic graph discovery, authoring and transaction protocols; consumer code supplies execution semantics through explicit registration. See `AgentWorkflow.md` for the operating sequence.

## Installation and dependencies

MCP is bundled in com.harufamily.dependencycore.graphkit 1.2.0 under Mcp/Editor and Mcp/Tests/Editor. There is no separate GraphMcp package to install. GraphKit's package manifest has no external dependencies; its core runtime/editor assemblies do not reference MCP or Newtonsoft JSON. The existing optional assembly names HaruFamily.Tools.GraphMcp.Editor and HaruFamily.Tools.GraphMcp.Editor.Tests are retained for consumer compatibility.

Both optional assemblies use versionDefines AND defineConstraints: MCP for Unity [10.3.0,11.0.0), Newtonsoft JSON [3.0.2,4.0.0), and UNITY_6000_3_OR_NEWER. Without these prerequisites Unity excludes the integration and its tests, including their unavailable assembly references. Do not add a mandatory MCP/JSON dependency to GraphKit. Core support metadata stays at its existing Unity floor; optional MCP is verified on 6000.3.12f1 only.

Window/HaruGraph/AI and MCP Setup lives in the ordinary GraphKit Editor assembly, works without the bridge, and offers explicit bridge installation, project-local skill export and documentation. Existing bridge installations are not overwritten. Setup does not edit AI client settings; AgentDock or MCP for Unity owns the connection. See [Installation.md](Installation.md).

The portable skill is at AgentSkill~/harugraph-mcp. Distribution~/setup.py installs GraphKit alone by default; --with-mcp opts into pinned bridge/JSON sources, --skill-only exports without changing dependencies. GraphKit testables enables core tests and, when its prerequisites exist, optional MCP tests. Release creation is described in [Publishing.md](Publishing.md).

## Authoring contracts

Document discovery uses direct serialized `IGraphDocument` fields, `IEditableGraphAsset`, or explicitly registered bindings; supported operations use `HGDocumentSession`, shared metadata and slot contracts. Preserve Action, Formula, Token, Asset and Property semantics. Do not introduce per-project node whitelists into discovery or editing.

Draft writes require the current generation. Keys from endpoints and root/list queries expire after mutations. `apply_batch` is atomic only within one draft, keeps one Undo entry and requires fresh query references after edits. `check` accepts only read operations. `validate` is structure/document diagnostics, not execution.

Property write targets remain serialized endpoints without canvas ports. For these endpoints, `check_connection` checks the selected Property source's family; `connect` selects its library definition through `HGDocumentSession.SetPropertyTarget`, and `disconnect` clears it. Selection creates an independent writer reference and preserves existing reader carriers and their modes. Ordinary read connections still use the Port registry.

> Trace: verified 2026-11-01 · consumer doc `Doc/2026-11-0/REPORT_貪噬鯢A循環與Property目標MCP修正.md`（live MCP selection, Undo/Redo, clear and saved-content readback; Test Runner pending）

Single-file saves use preview/commit or preview_save_as/save_as. Referenced graph assets retain the single-file blocker; use `haru_graph_transaction` for a complete dependency-aware commit. Keep source-memory/disk/window conflicts and missing-reference checks. Up to 16 drafts can be open.

`RegisterDocuments` returns a disposable registration lease. Each provider supplies at most 64 uniquely keyed `HaruGraphDocumentEntry` values per Owner, with a binding and optional `HGEditorExtensionContext`. Discovery IDs combine persistent Owner identity, provider ID and entry key; bindings can address nested/multiple documents. A provider must return stable keys, read without mutation, write only its document, and retain the submitted reference. Discovery still scans ScriptableObject asset files, not scene components. Disposing a registration prevents future discovery; existing sessions retain their binding/context.

Managed-reference fields and list elements use `structured_types` followed by `replace_structured_value` (exact returned type, no value payload). Clear with `value:null` and no type. Only assignable, serializable data classes with a public default constructor are offered; graph carriers, Unity objects, delegates and document internals are excluded. Connected nested graph references must be disconnected before replacement/removal. `initialize_structured_value` initializes ordinary records/lists; managed-reference instances require explicit type selection. Paths and keys must be requeried after mutation.

Registered metadata replaces reflection for a described node. `metadata_fields`/`set_metadata_value` expose visible scalar descriptors; `value_fields`/`set_value` respect the same descriptor IDs. `structured_fields` follows descriptor IDs into composite data and preserves visibility/read-only constraints. A consumer may declare `managedReference:true` on HGFieldDescriptor.Create when its setter persists managed-reference data; the default is false and must not be inferred from the runtime type. Slot operations remain typed graph commands; JSON cannot replace arbitrary graph internals.

## Consumer execution integration

Execution contracts are registered with `RegisterExecutor(IHaruGraphExecutor)`. `haru_graph_execute` provides `providers`, `describe` and `run`; all require a current draft/generation. A provider describes its input, result and isolation contract and receives a detached snapshot, copied input and cooperative cancellation token. Input JSON text is bounded to 262144 .NET string characters and response JSON text to 1048576 characters; these are character limits, not encoded byte limits. Timeout is bounded to 1..30000 ms. The provider must validate inputs, honor cancellation where feasible, and clean up in finally blocks. A successful invocation is not a statement that provider-specific assertions passed: inspect its result. Duplicate IDs fail; disposal unregisters; integrations register again after domain reload. No executor is discovered by scanning arbitrary graph classes.

A consumer may also keep its separately named tool. `HaruGraphDraft.WithDraftSnapshot` checks draft identity, expiration, generation and Editor readiness, then supplies a `HaruGraphDraftReadContext` with a copied document and endpoint dictionaries copied together. Node IDs and current endpoint keys address the same copied graph. The consumer receives neither the owning session nor its commit API.

Unity object references remain shared: a detached graph is not a sandbox for external assets or world effects. Consumers must audit/copy dependencies and manage their own context, cancellation, disposal and assertions. Only explicitly registered trusted Editor providers can be selected; no process sandbox or forcible preemption is provided. The value codec (`TryValue`/`WireValue`) and persistent identity helper (`HaruGraphInspect.OwnerKey`) are reusable transport helpers, not permission to execute nodes.

## Validation

Verify the assembly reference boundary and run protocol-level transactions with multiple consumers, including a custom document. Do not infer portability from one game's execution tests. Record separately compilation in the host, consumer protocol coverage, and testing in a separate clean project.

The package includes `HaruFamily.Tools.GraphMcp.Editor.Tests` under `Mcp/Tests/Editor`. Add `com.harufamily.dependencycore.graphkit` to the consuming manifest's `testables` to run it. These tests check the compiled integration and test assembly reference allowlist, reject consumer execution actions through the public command entry, and verify invalid snapshot requests cannot invoke a consumer. They need no game definitions, LogicGraph or AssetPipeline. The allowlist covers direct references, not a transitive dependency audit.

Consumer transaction and persistence tests remain in the host integration suite because they intentionally depend on consumer document types. Package boundary tests do not replace those workflows or constitute a clean-project installation test.

Changes here affect static Editor tools, not the serialized GraphKit carrier namespace or assembly. Preserve script `.meta` identities when moving existing files. Do not add serialized compatibility shims for the transport's former source location.

## Reproducible independent installation

The package includes a minimal custom `IGraphDocument` protocol test. It discovers a temporary Owner without a window, edits a field on a detached draft, checks stale generation rejection and Undo/Redo, validates, commits with a preview token, unloads/reimports the asset, and reopens it. It cleans its temporary assets and snapshots and requires no consumer framework.

`Validation~/prepare_clean_project.py --source-project <resolved-host> --output <new-directory>` creates an independent installation probe. The output must not exist and must be outside the host. It copies only GraphKit and resolved MCP/JSON/test packages and their dependency closure (--without-mcp omits the bridge and JSON); it never copies host Assets, settings (except the Editor version), or Library. Package source versions and SHA-256 digests are recorded in `installation-sources.json`. Built-in packages are supplied by the Editor. Run the printed Unity CLI test command with the installed Editor, optionally passing `-- -nographics -logFile <log-path>`.

This is a local/offline package installation check. It does not verify registry publication, Git download, a new MCP client connection, or every Unity version advertised by package metadata. Keep those claims separate from protocol entry-point tests. On 2026-10-30 this procedure was verified with Unity 6000.3.12f1; see the project handoff report for results and artifact paths.

## Multi-document and dependent-asset commits

`haru_graph_transaction.dependencies` accepts draft/generation pairs and returns the reverse dependency closure and document IDs for each affected file. Open and include every document explicitly, including unchanged dependents; unknown file formats, scenes, Packages and files without editable graph bindings block the transaction. This conservative file-level closure never silently edits arbitrary project data.

`preview` accepts 1..16 unique persistent drafts and issues a single-use, ten-minute token. Review member hashes, affected files and each draft's capture/history. `commit` rechecks generations, graph fingerprints, memory/disk revisions, dependency closure, dirty windows, editability and the shared session guards. It stages all document clones with invalidated validation state, validates the staged references, then saves only selected asset files. Same-file multiple documents are supported. New unsaved Owners use save_as first.

The GraphKit `CommitAssetsPreservingOwners` API owns the multi-file write and rollback; GraphKit does not depend on MCP. File backups (32 MiB/file, 128 MiB total), serialized Owner memory and a path/state journal are prepared before writes. Errors restore all possible files and Owners, verify restoration and report RollbackSucceeded plus RecoveryDirectory. Successful commits consume drafts; after a write attempt all sessions must be reopened. The backup directory is `Library/GraphKitAssetRecovery/<id>` and is retained for review; `paths.txt` maps ordered entries to `<index>.before`, and state.txt records prepared/saving/committed/rolled_back. Do not restore an old backup over later user edits without comparison.

This is compensating atomicity within the running Editor, not a crash-atomic filesystem/database transaction. After a process crash or failed rollback, inspect the returned/persisted recovery directory and affected assets before any further write. Binding callbacks and validators are trusted Editor code and must honor their mutation contracts.

## Flow authoring and background layout review

`flow` returns paged declared sections and step uses, including section_path/step paths, inputs, sharing and unknown/cycle diagnostics. Paths are generation-local, not node IDs or coordinates. Unknown containers and external graph references remain boundaries. `flow_candidates` returns exact action types and declared child keys; constructor/description failures are explicit description_error values. `flow_insert`, `flow_remove`, `flow_transfer` and `flow_wrap` run through HGDocumentSession, using the same structure planner as the window. Transfer takes source section_path, index/count, target_section_path and destination_index: the latter is a pre-removal gap 0..Count, not the final index used by move_slot. Wrapping accepts only empty declared Conditional/Loop containers and a compatible child_section_key. Original slots, flags and shared carriers are moved, not copied. New wrapper inputs require explicit configuration.

Flow list writes verify actual serialized mutable lists and visible/writable metadata on detached builds. Self/descendant moves, incompatible families and stale generations are rejected. Insert/remove use ordinary session rollback and orphan rules. apply_batch treats section_path, target_section_path and flow_path as generation-local keys: after an earlier mutation, query flow again and use a $ref to that fresh result. A batch has one Undo entry and rolls back on error. flow/flow_candidates/geometry/layout_diff are allowed read-only check queries. `protect` cannot be batched.

`geometry` measures a detached copy with HGGraph, the built-in canvas node/row metrics, built-in port anchors and the registered port provider. Pass a scope_id from group_scopes. Optional group_id/tab_index virtually chooses a tab; other groups use their stored collapsed/active state. Only direct off-tab members are hidden, so shared/external sources remain visible. No HaruGraphWindow is created, focused, resized, zoomed or sent GUI events; Owner and visible tabs are untouched. Generated IDs are marked preview_only_id and must not be used for editing. Results contain graph-space bounds, ports, connected anchors, overlap pairs and straight_distance_estimate, not true routed-wire lengths. Boundary/hidden edges are counted and omitted.

`layout_preview` adds SVG text built from the measured rectangles and anchors. This is a measured schematic, not a Unity canvas screenshot: custom controls, group frames, hidden-edge ghosts, exact wire routing and text clipping are omitted. Crossings and text clipping are explicitly unmeasured. Geometry is limited to 512 visible nodes, 4096 links, 8192 ports and a one-million-character response; SVG additionally bounds coordinates to ten million units. No visual acceptance claim follows from geometry, SVG or validate.

`layout_diff` compares current final state with the opened/last committed baseline. Canonical reference traversal ignores GraphViewState and GraphNode position/HasPos fields for content, preserving IDs, aliases, hidden fields, lists, orphan pools and external reference identities. Both sides first invalidate the consumer's declared validation state on detached copies, matching normal save behavior; consumers must keep InvalidateValidation limited to validation state. Full state comparisons retain validation/view data. Node-closure changes are sampled up to 256 identities, returning at most 64 changed entries with explicit completeness/truncation; full content hashes remain authoritative. Comparison rejects incomplete snapshots and exceeds neither 256 nesting levels nor 8 MiB canonical text. Unity object references remain shared and callbacks/providers remain trusted Editor code.

`protect` fixes a policy at the current draft generation, with layout_only and/or up to 64 protected_node_ids / 64 protected_group_ids. Node protection includes the full reachable closure and incoming reference locations; groups protect the entire group, all tabs, member nodes and their dependencies. This is conservative whole-group protection, not independently editable protected tabs. Resolve IDs from the current draft, not a prior asset version. Missing/ambiguous carrier members (including unsupported HEAD identities) reject policy creation. External Unity objects are protected by reference identity, not by recursive asset-content snapshots; give other edited dependency drafts their own policies. Policies cannot be relaxed or replaced; discard/reopen to choose another. Policies are draft-local and do not lock another user's Editor or future draft.

Single writes use RunAtomic and check policy before accepting; batches check their final state in the existing atomic transaction. Rejection restores document/history and invalidates preview tokens; requery generation and paths. Undo/Redo controls are disabled while policy is active to prevent crossing its baseline; history remains inspectable. preview/commit, Save As and dependency transaction preflight recheck policy on a detached save-staged copy after validation invalidation. Original source/disk/window/dependency guards remain in force. A failed verification stops persistence. After saving, reopen and reread as usual; a pre-save layout_diff is not a disk verification.

> Trace: verified 2026-10-31 · doc `Doc/2026-10-2/REPORT_GraphKit混合式第五階段.md`（來源靜態核對；Unity 編譯／協定測試與視覺待驗證）

## Hybrid flow consumer acceptance

The host HybridFlowProtocolRoundTripsAcrossLogicGraphAndAssetPipeline test exercises both real consumer families through public protocol commands and save/reimport/reopen. The portable UndeclaredCustomContainerStopsFlowExpansionWhileNodeGeometryRemainsReadable test checks unknown-flow fallback without a consumer reference. Boundary tests also inspect compiled Runtime/Editor core dependencies, separately from optional integration dependencies. The complete applicable matrix and evidence checklist is [HybridFlowValidation.md](../HybridFlowValidation.md); source tests are not a claim that Unity, transport, clean installation or visual acceptance has passed.

> Trace: verified 2026-10-31 · doc `Doc/2026-10-2/REPORT_GraphKit混合式第六階段.md`（來源靜態核對；Unity／協定／視覺待驗證）

Test fixtures must follow the same candidate contract as production callers: HGTypeIndex excludes assemblies referencing nunit.framework. AuthoringTypes additionally permits compatible, public-default-constructible types already present in the current document. A protocol test that needs a test-only wrapper first owns a prototype in its temporary document, queries flow_candidates, and uses the returned exact type. Do not bypass the production catalog filter or load arbitrary input types to make a test pass.

> Trace: verified 2026-10-31 · doc `Doc/2026-10-2/REPORT_GraphKit混合式第六階段.md`（測試回報追查；修正後待重跑）

Background geometry follows the original canvas multilevel slot and action-list nesting layout. Node entries include parent_node_id; ordinary ancestor/descendant containment is excluded from overlap pairs and internal nested-slot edges are omitted from schematic wiring. Data endpoints and sharing remain unchanged. Scope and first-version limits are specified in the core Maintenance.md section 原畫布的巢狀 Node 與動作清單.

CaptureLayout passes the detached document's Folded and Unfolded view-state keys to HGGraph.Build. Saved composite-list element and nested-list collapse therefore affect measured visibility and height, while data endpoints remain available independently of canvas visibility. BackgroundGeometryHonorsSavedCompositeAndListCollapseWithoutRemovingDataEndpoints covers both saved-collapse paths through geometry/layout_preview and verifies that reads retain draft generation and content. Tests require Unity execution; source presence is not a passing result. Slot/class decorative frames remain outside the schematic contract.

> Trace: verified 2026-10-31 · doc `Doc/2026-10-2/REPORT_GraphKit槽位巢狀Node第一版.md`（原始碼靜態核對；Unity／拖放與視覺待驗證）

## Per-node composition and connection modes

Read `nodes` or source entries in `endpoints`: `node_mode` is `Composition` or `Connection`, and `read_user_count` counts distinct read Slots (excluding Property write targets and duplicate Union wrappers). New carriers default to Composition. Dragging a Node into a Slot/list selects Composition; dragging a Port wire selects Connection automatically, preserving the same carrier and allowing one-to-many reads. The UI has no mode selection menu. Dragging a nested Node onto empty canvas preserves its references and selects Connection.

MCP `connect` represents Port wiring: it selects Connection in the same atomic edit, including when reconnecting the same source. Property write-target selection does not change the reader carrier's mode. `check_connection` checks actual compatibility without changing mode. Requery generation and endpoints after every successful edit. Connection stays external even with one reader; it never becomes Composition merely because other readers disconnect.

For background composition authoring, use `configure_node` with the current draft/generation, exact node_id and node_mode:"Composition". Disconnect extra read Slots first; multiple readers reject the change without automatic copying/disconnection. Mode changes are content edits and obey normal Undo/Redo, atomic batches, protection and persistence guards. Optional position places a standalone source. Mode does not override type, cycle, group/tab or nesting constraints.
