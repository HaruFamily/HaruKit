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
- `IGraphHead` / `IOrphanPool` / `ITokenOwner` / `IGraphDocument` are the
  contracts the editor walks. The default editor metadata adapter uses
  reflection for arbitrary POCO fields and GraphKit attributes; document and
  graph traversal use the contracts directly.
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
  one `IGraphDocument` field on the owner.
- `HaruGraphWindow.OpenForDocument(owner, binding)` opens the exact document
  selected by a Tool. The binding supplies typed read, create, clone, and write
  delegates; GraphKit keeps the working copy, Undo, validation, and persistence
  transaction.
- Tool-specific editor extensions are passed through
  `HGEditorExtensionContext`. A provider adds ports with `AddInput`,
  `AddOutput`, or `AddAggregate`; the build scope supplies the generation and
  rejects duplicate or incomplete endpoints.
- `HGPortConnection.Check` is the shared compatibility result for rendered
  ports. `CheckInputSource` applies the same input acceptance rules to direct
  Asset, Token, and Catalog drops that do not yet have a `GraphNode` carrier.
- `HGDocumentSession<TDocument>` is the public non-window transaction for a
  Tool that needs a cloned document, generation-scoped connect/disconnect,
  undo/redo, commit, and cancel. Pair it with `HGPortRegistry` to register
  bounded custom Ports without receiving a mutable `HGGraphView`.
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

For non-window commands, open a public working-copy session. Ports and all
handles from a registry are valid only for that session generation; create a
new registry after every changed command, undo, redo, commit, or cancel.

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

An `IHGEditorExtensionProvider` receives an `HGPortBuildContext` while the
window builds its current generation. Register custom ports with `AddInput`,
`AddOutput`, or `AddAggregate`; do not access the obsolete `Graph` escape
hatch. A custom port may be a visual alias for an existing `GraphNode`, but it
must not claim to persist a separate output identity.

For Tool-owned node data, provide an `IHGEditorMetadataProvider`. A complete
`HGNodeDescriptor` replaces reflection for that node type. Register a custom
`IHGValueDrawer` for a value type whose control is not supplied by
`HGValueField`; its `Measure` and `Draw` methods receive the same descriptor
context and width, and the returned `HGValueDrawerResult` is written through
the descriptor transaction.

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

`Editor/Tests/HGPublicConsumerTests.cs` is the compile-time third-consumer
example: it opens document B, registers custom input/output ports, exercises
generation rejection and undo/redo, commits/reopens/cancels, and reports a
domain diagnostic without implementation-model access.

## Validation Status

The public API examples have static source coverage. Unity compilation,
EditMode execution, and visual interaction validation remain environment-run
steps; see the GraphKit core implementation report for the current T/U case
status.
