# GraphKit

Current package version: `1.1.0` (Unity 2021.3+).

GraphKit is a Unity UPM framework for authoring serialized node graphs. It
provides the graph carrier, the non-generic contracts an editor needs to walk a
graph, and a complete IMGUI node editor. It does not schedule or evaluate nodes;
optional execution observation records the lifecycle reported by a consumer.

Any domain can expose an `IGraphDocument` through a serialized field or an
explicit editor-side `HGDocumentBinding<TDocument>`. `LogicGraph` is one such
consumer; it is not required here.

## Features

- `GraphNode` is the single carrier: identity, position, note, disabled state,
  and one of five content kinds (empty, inline body, shared asset, named
  token, catalog). Swapping the source keeps the id, layout, and every inbound
  edge.
- `GraphToken` is a named token with its own canvas and candidate pool.
  Referencing nodes store the object, never a name string.
- `IGraphHead` / `IOrphanPool` / `ITokenOwner` / `IGraphDocument` expose carrier
  and document access. Arbitrary POCO traversal, default metadata and deep copies
  retain reflection at their adapters; known Slot and carrier operations use
  the contracts directly.
- Root wording comes from the document, not the editor: `RootChip` is the badge
  on the root node, `RootNoun` is the word the editor drops into menus, hints,
  and logs. No domain term is hard-coded in the window.
- `FormulaSlotBase` and `ActionSlotBase` are zero-field non-generic bases, so a
  consumer can add generic execution subclasses without changing the serialized
  format.
- `CatalogSlotBase`, `CatalogCellBase`, and the catalog contracts support
  container-style nodes whose inline cells provide typed outputs. GraphKit owns
  only the structure; the consuming Tool owns catalog data and evaluation.
- `GraphDeepCopy` preserves polymorphic `SerializeReference` graphs, shared
  references, cycles, and Unity object references.
- The included editor window uses GraphKit attributes only and has no Odin
  dependency.

## Requirements

- Unity 2021.3 or later

No external packages. The editor assembly references only the runtime assembly.

## Install

Add this Git URL in Unity Package Manager:

```
https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.harufamily.dependencycore.graphkit": "https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit"
  }
}
```

For reproducible builds, pin the package URL to a release tag or commit.

## Assemblies

| Assembly | Platforms | References |
|---|---|---|
| `HaruFamily.DependencyCore.GraphKit` | all | — |
| `HaruFamily.DependencyCore.GraphKit.Editor` | Editor | `HaruFamily.DependencyCore.GraphKit` |

## Namespace

Runtime types live in `HaruFamily.DependencyCore.GraphKit`; editor types live
in `HaruFamily.DependencyCore.GraphKit.Editor`. Serialized type identities are
data contracts and are not renamed as routine cleanup.

## Editor Integration

The editor works on a deep-copied document after cleaning missing-type data.
Save validates and writes a new copy back to the owner only on success; Cancel
discards the working copy. Undo/Redo and Port
handles are scoped to that document session and its current generation.

- `HaruGraphWindow.OpenFor(owner)` keeps the legacy convenience path and finds
  exactly one `IGraphDocument` field on the owner. It rejects ambiguous owners;
  Tools must use `OpenForDocument` when an owner has multiple documents.
- `HaruGraphWindow.OpenForDocument(owner, binding)` opens the exact document
  selected by a Tool. The binding supplies typed read, optional create, and write
  delegates; cloning uses the document's `DeepCopy` with type and alias checks.
  GraphKit keeps the working copy, Undo, validation, and persistence
  transaction.
- Tool-specific editor extensions are passed through
   `HGEditorExtensionContext`. A provider adds ports with `AddInput`,
  `AddOutput`, or `AddAggregate`; the build scope supplies the generation and
  rejects duplicate or incomplete endpoints.
- During that build, a provider can query the read-only `Nodes`, `Fields`, and
  `Ports` snapshots or resolve one `HGPortDescriptor` by key. These snapshots
  expose stable ids and current geometry, never `HGNodeView`, `HGRow`, or the
  mutable Port indexes.
- Build a custom `HGDelegatePortPresentation` from an `HGPortAnchor` snapshot
  to participate in node-local snapping and row lookup without retaining an
  Editor view object. A presentation without an anchor remains a global-only
  endpoint.
- Each persisted source has one primary output, initially registered with
  `isPrimaryOutput: true`. Link rebuilding uses this mapping; visual aliases
  do not become primary merely because they were registered later.
- A provider can add a presentation with `AddInputAlias` / `AddOutputAlias`, then
  explicitly select it with `SelectPrimaryInput` / `SelectPrimaryOutput`. One
  explicit selection per Slot/source replaces the built-in default; a second
  selection is rejected. Input links resolve by Slot identity, not the alias key.
  `CreatePresentation(existingKey, offset)` follows current geometry, visibility
  and locking without exposing a mutable view. Custom bindings in a window must
  already be part of the built graph (use a Slot descriptor for custom fields).
- `HGPortConnection.Check` is the shared compatibility result for rendered
  ports. `CheckInputSource` applies the same input acceptance rules to direct
  Asset, Token, and Catalog drops that do not yet have a `GraphNode` carrier.
- A Tool policy can implement `IHGPortConnectionPolicy` to return a specific
  `HGPortConnectionResult`, such as `DomainRejected` or `WouldCreateCycle`.
  Existing `IHGPortPolicy.CanAccept` implementations remain supported and map
  a false result to `Rejected`.
- A Tool source can implement `IHGPortSourceAcceptance`, or use the optional
  detailed delegate on `HGDelegatePortSource`, so the same result survives Port
  linking and direct Asset, Token, or Catalog drops.
- `HGDocumentSession<TDocument>` is the public non-window transaction for a
  Tool that needs a cloned document, generation-scoped connect/disconnect/
  replace-source/delete-node commands, undo/redo, commit, and cancel. Create
  its registry through `session.CreatePortRegistry()`; a registry belongs to
  that exact session and generation, so it cannot be submitted to another
  document session. This registers bounded custom Ports without receiving a
  mutable `HGGraphView`.
- `window.GetDocumentCommands()` returns an `HGWindowSession` for the actual
  window pipeline. Its immutable `Query()` snapshot contains generation, nodes,
  ports and resolved link keys. Connect/disconnect, descriptor value edits,
  undo/redo, validation, commit and document cancellation use the same routes as
  the window UI. Rebinding the window invalidates the command object. This is
  the integration entry for Tools extending the visual editor; the non-window
  `HGDocumentSession<TDocument>` owns an independent editing history.
- A Tool may provide `IHGEditorDiagnosticProvider` through its explicit
  extension context. Runtime Owners with project rules can additionally expose
  `IGraphDomainDiagnostics`; both return `GraphDiagnostic` pure data rather
  than Editor rows or windows.

## Public API Patterns

### Execution observation and Hold

`IGraphExecutionDocument` optionally exposes a `GraphExecutionSource` and an
`ExecutionRevision`. Runtime copies should share the source while retaining the
revision of their own content. GraphKit's source/session/visit types use standard
`Task` and `CancellationToken`; they do not depend on LogicGraph or UniTask.

An observer calls `source.Observe()` and disposes the returned lease when done.
A consumer starts a `GraphExecutionSession` for one execution chain, then reports
each invocation with `session.Enter(new GraphExecutionNodeKey(nodeId, scope))`.
Await `visit.WaitAsync()` **before** executing the body, and report `Complete()`,
`Fail(exception)` or `Cancel()` afterwards. Dispose unfinished visits/sessions.
These APIs are main-thread APIs; token cancellation may originate elsewhere, but
consumer continuations and lifecycle reports must return to the main thread.

Holds belong to a specific session. Prepared Holds are consumed by the next
execution with the matching revision, not broadcast to every runtime copy.
Snapshots distinguish not visited, holding, running, completed, failed and
cancelled, including counts for repeated/overlapping visits. Scope-qualified
keys keep asset-internal nodes separate from caller nodes. Sources retain 32
finished sessions and never evict active sessions. The final observer leaving
releases Holds; explicit cancellation ends the chain instead of resuming it.

The window automatically observes documents exposing this capability. During
Play, select **準備下一次執行**, set node Holds, then trigger the consumer's graph.
The next execution is selected automatically; subsequent executions remain
independent and can be selected from the execution panel. `Ⅱ` arms Hold; `▶`
releases it, with a lit background while actually waiting. Closing the observer
releases its Holds when no other observer remains. Leaving Play or reloading
assemblies cancels observed chains. Holds/history are transient; after Domain
Reload reopen the document through its Tool entry and prepare Holds again.

Header controls use `●` enabled / `○` disabled. Controls appear on hover or
selection, with disabled and armed Hold indicators remaining visible. Comment
icons do not remain visible in the collapsed toolbar; expanded comments remain
in the node body. Control slots keep fixed widths. A single strip under the
24px Header shows errors first, then selected execution state, then editor
warnings: red error/failure, amber Hold/warning, cyan running, muted green
completed, neutral not-visited/cancelled. Tooltips retain the detailed reason.
Unsaved documents, mismatched revisions or replaced asset roots suppress live
colors/new Holds; release/cancel controls remain available. The strip does not
replace node identity colors, selection borders or field-level diagnostics.
An execution reporting `graphkit.execution.node-id-missing` also suppresses the
projection: save the authored graph before creating runtime copies, rather than
mistaking an unmapped node for a node that never executed.

### Document commands

Use one typed binding per document field. The document id is stable within its
owner, so separate bindings keep their working copies, undo histories, and
commits separate.

```csharp
var binding = new HGDocumentBinding<MyDocument>(
    "MyTool.GraphB",
    owner => ((MyOwner)owner).GraphB,
    (owner, document) => ((MyOwner)owner).GraphB = document,
    MyDocument.Create);

HaruGraphWindow.OpenForDocument(owner, binding, extensions);
```

Use a second binding with a different `DocumentId` and getter/setter for a
second document on the same owner. Do not route either document through legacy
field discovery when the Tool knows which field it is editing.

### Commit safety

Window document saves (including legacy field bindings) and non-window sessions
check the Owner for missing `SerializeReference` types and compare the live
document reference with the baseline captured at open, successful write, or
cancel. A replaced document returns `HGSessionCommandResult.Conflict`; the
working copy and its history remain available. Preserve any edits you need
before cancelling to adopt the latest Owner document. Validation is followed by
another Owner check immediately before writing.

Missing-type cleanup happens before cloning/opening and before validation/commit,
without backups or confirmation dialogs. Unity's missing managed-reference IDs
identify affected carriers and list entries; broken Action entries and references
are removed. Cleanup also handles previously persisted Inline/Catalog/Token
carriers with missing content after Unity's missing-type records were already
cleared. Normal Empty carriers and unassigned Action slots are retained.
When a missing CatalogCell filter changes its output type, only
incompatible users of that affected Cell are disconnected. Unrelated empty nodes
and incompatible connections are not treated as missing data.

When Unity exposes a missing reference as the ordinary null ID (`-2`), cleanup
reads the Owner's original Unity text-serialized v2 `rid` links to locate it.
This is read-only, scoped to the Owner's local file ID, and does not classify
ordinary null references as missing. Unavailable or unsupported source data is
reported before clearing; the asset file is not edited by the location reader.

Cleanup changes memory and marks the document dirty; it does not save the asset.
Window saves, `HGModel.Save()` and session `Commit()` all require validation to
pass. Invalid graphs are never saved as drafts. Conflict, clone and write failures
also stop the save. Cleanup during an active session clears history containing the
removed references, while retaining current edits. The save label stays **存檔**;
only actual unsaved changes highlight it.

Reference checks do not detect in-place changes to the same document instance.
Tools with other editing entry points can supply a document-scoped revision:

```csharp
var binding = new HGDocumentBinding<MyDocument>(
    "MyTool.GraphB",
    owner => ((MyOwner)owner).GraphB,
    (owner, document) => ((MyOwner)owner).GraphB = document,
    create: MyDocument.Create,
    readRevision: owner => ((MyOwner)owner).GraphBRevision.ToString());
```

Every external in-place edit must update that revision. Its getter must be pure;
do not use an Owner-wide dirty flag, which also changes for unrelated fields or
catalogs. The original constructor remains available without a revision reader.

The binding setter must assign the supplied document reference, without mutating
the previous document or unrelated Owner data. It must also accept `null` when
recovering a document that was initially absent. If it fails, GraphKit attempts
to restore the original reference and reads it back. Unconfirmed recovery emits
`graphkit.commit.recovery-required` and blocks subsequent commits until the
Owner has been inspected/repaired and explicitly reloaded. This compensation is
not a rollback of arbitrary setter side effects or disk writes. Persistence
failures keep the working copy/history; the Owner may already hold the new
document, so inspect the diagnostic before retrying.

Non-window sessions expose failures through `LastDiagnostic`. Window commands
return the failure category and expose the last commit diagnostic through
`Validate()`; the UI displays it in the Console. Failed reloads preserve the
working copy and history. Non-window Undo keeps the latest 40 steps, with each
successful mutation being one step; window history retains its existing merge
policy.

For non-window commands, open a public working-copy session. Create the
registry through that session, because Ports and all handles are valid only for
its exact document and generation. Create a new registry after every changed
command, undo, redo, commit, or cancel.

```csharp
if (HGDocumentSession<MyDocument>.TryOpen(owner, binding, out var session))
{
    var ports = session.CreatePortRegistry();
    ports.AddInput(inputKey, inputSlot, inputPolicy, inputPresentation);
    ports.AddOutput(outputKey, outputSource, outputPolicy, outputPresentation);
    var result = session.Connect(ports, outputKey, inputKey);
    if (result == HGSessionCommandResult.Changed)
        session.Commit();
}
```

`ReconnectInput` takes a registered input key and an accepted source whose
`GraphNode` is already reachable from the session document roots, tokens, or
candidate pool. Both input and source must belong to the current working copy;
a current registry cannot authorize a foreign or stale Slot. The input-key
`ReplaceSource` overload forwards to `ReconnectInput` for source compatibility.

`ReplaceSource(carrier, HGCarrierSource)` changes the content of a document-owned
carrier in place. Body, Catalog, Asset and NamedToken source descriptions retain
the carrier id, position, note and inbound references; incompatible users reject
the transaction. Asset parameter bindings are retained by family plus name.

Pass an extension context to the diagnostic overload of `TryOpen` to use the
same provider for `CollectDiagnostics()` and commit validation. `LastDiagnostic`
explains a rejected/failed command. Mutation failures restore the working-copy
snapshot and history, and advance generation to invalidate references to the
discarded copy. Commit never turns the editing document into the Owner instance.
Owner setters and filesystem writes remain external effects, not an ACID transaction.

`DeleteNode` accepts a document-owned carrier, disconnects every current user,
removes it from any inline owner and candidate pool, and preserves its direct
child sources as candidate roots. Rebuild the registry after it changes the
session generation.

An `IHGEditorExtensionProvider` receives an `HGPortBuildContext` while the
window builds its current generation. Register custom ports with `AddInput`,
`AddOutput`, or `AddAggregate`; do not use the obsolete `Graph` escape hatch or
legacy `Add(HGPort)` adapter. Use its read-only `Nodes`, `Fields`, `Ports`, and
descriptor lookups to anchor an extension without retaining Editor view
objects. A custom port may be a visual alias for an existing `GraphNode`, but it
must not claim to persist a separate output identity. Use explicit primary
selection to make that alias the endpoint used after rebuild:

```csharp
var presentation = context.CreatePresentation(existingOutputKey, new Vector2(12, 0));
if (context.AddOutputAlias(existingOutputKey, customOutputKey, presentation))
    context.SelectPrimaryOutput(customOutputKey);
```

Root titles are display text, never identity. String, enum, Guid and scalar root
keys have typed stable ids. Other key types must provide `HGRootGroupView.StableId`
through their root adapter. IDs must remain stable across clones/reorder/reopen;
duplicate identities or missing non-scalar identities are build diagnostics.

For Tool-owned node data, provide an `IHGEditorMetadataProvider`. A complete
`HGNodeDescriptor` replaces reflection for that node type. Register a custom
`IHGValueDrawer` for a value type whose control is not supplied by
`HGValueField`; its `Measure` and `Draw` methods receive the same descriptor
context and width, and the returned `HGValueDrawerResult` is written through
the descriptor transaction. Derive from `HGValueDrawer<T>` for a typed value
argument; use the object interface only at the adapter boundary. HideLabel and
list insets use the same geometry for measurement and drawing. Drawer failures
show a disabled field and a diagnostic, rather than throwing on every repaint;
after fixing a drawer, reopen through the Tool entry to clear its failure state.

```csharp
public sealed class PercentDrawer : IHGValueDrawer
{
    public float Measure(in HGValueDrawerContext context, float width) => 18f;
    public HGValueDrawerResult Draw(Rect rect, in HGValueDrawerContext context, object value)
        => HGValueDrawerResult.Unchanged(value);
}
```

Implement `IHGEditorDiagnosticProvider` on the explicit extension provider to
add working-copy `GraphDiagnostic` values. Use stable code and location data
such as `new GraphDiagnosticLocation(fieldPath: "Root")`; do not retain rows,
nodes, or window objects. Runtime owner rules can use `IGraphDomainDiagnostics`
instead. GraphKit runs these providers during bind, live validation, and commit
validation.

`Editor/Tests/HGPublicConsumerTests.cs` contains public-only consumer sources for
both non-window and actual window commands. The window case registers aliases
through the real provider, selects primary endpoints, rebuilds links, edits a
descriptor value with Undo/Redo, blocks domain errors, and commits/rebinds/cancels.
These are in-memory Owner round-trips; Unity asset serialization and GUI gestures
still require the documented manual checks. AssetPipeline's test assembly also
consumes the public window commands without GraphKit friend-assembly access.

## Tests And Validation

`Editor/Tests` covers the public Port model, metadata and custom drawers,
diagnostics, document sessions, ownership/generation guards, source replacement,
node deletion, commit validation, and public window commands. AssetPipeline's
Editor tests also consume GraphKit through its public API without friend-assembly
access.

These tests are intended for Unity Test Runner EditMode. GUI gestures, asset
serialization round-trips, and visual layout still require validation in a Unity
project that installs the package.
