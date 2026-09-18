# GraphKit

GraphKit is a Unity UPM framework for authoring serialized node graphs. It
provides the graph carrier, the non-generic contracts an editor needs to walk a
graph, and a complete IMGUI node editor. It defines no execution semantics: it
does not know what a node does, when it runs, or what it returns.

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

## Validation Status

The public API examples have static source coverage. Unity compilation,
EditMode execution, and visual interaction validation remain environment-run
steps; see the GraphKit core implementation report for the current T/U case
status.
