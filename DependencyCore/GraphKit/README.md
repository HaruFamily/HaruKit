# GraphKit

GraphKit is a Unity UPM framework for authoring serialized node graphs. It
provides the graph carrier, the non-generic contracts an editor needs to walk a
graph, and a complete IMGUI node editor. It defines no execution semantics: it
does not know what a node does, when it runs, or what it returns.

Any domain that implements `IGraphDocument` on a serialized field gets the node
editor for free. `LogicGraph` is one such consumer; it is not required here.

## Features

- `GraphNode` is the single carrier: identity, position, note, disabled state,
  and one of four content kinds (empty, inline body, shared asset, named
  endpoint). Swapping the source keeps the id, layout, and every inbound edge.
- `GraphEndpoint` is a named variable with its own canvas and candidate pool.
  Referencing nodes store the object, never a name string.
- `IGraphHead` / `IOrphanPool` / `IEndpointOwner` / `IGraphDocument` are the
  contracts the editor walks. No member-name reflection anywhere.
- Root wording comes from the document, not the editor: `RootChip` is the badge
  on the root node, `RootNoun` is the word the editor drops into menus, hints,
  and logs. No domain term is hard-coded in the window.
- `FormulaSlotBase` and `ActionSlotBase` are zero-field non-generic bases, so a
  consumer can add generic execution subclasses without changing the serialized
  format.
- `LogicGraphDeepCopy` preserves polymorphic `SerializeReference` graphs, shared
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

Types live in `HaruFamily.Framework.LogicGraph`. The namespace predates the
split and is kept deliberately: `GraphNode` and `GraphEndpoint` carry
`[MovedFrom]` for the assembly move only, so existing serialized assets migrate
without touching their recorded namespace.
