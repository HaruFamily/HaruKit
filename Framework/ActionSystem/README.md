# ActionSystem

ActionSystem is a Unity UPM framework for authoring and executing serialized
action graphs. It provides timing-based action dispatch, typed formulas, named
formula endpoints, reusable ScriptableObject graphs, validation, deep copying,
and an editor graph interface. It does not define domain types, timing values,
or action behavior.

## Features

- `ActionSystem<TTiming, TPack>` dispatches asynchronous actions for a
  consumer-defined timing enum and execution context.
- `ActionBase<TPack>` performs side effects; `FormulaBase<TResult, TPack>`
  asynchronously evaluates typed values.
- Slots can use a constant fallback, an inline graph node, a reusable asset,
  or, for formulas, a named endpoint.
- `GraphNode` preserves its identity, notes, disabled state, and editor layout
  while its source changes.
- Action and formula assets expose named endpoints as parameters. Each asset
  invocation receives an isolated token scope with optional caller bindings.
- Editor validation detects invalid node sources, duplicate timings or endpoint
  names, incompatible bindings, and graph or asset cycles before execution.
- `DeepCopy()` preserves polymorphic `SerializeReference` graphs, shared
  references, cycles, and Unity object references.
- The included graph editor uses ActionSystem attributes and has no Odin
  dependency.

## Requirements

- Unity 2021.3 or later
- [UniTask](https://github.com/Cysharp/UniTask):
  `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`

Unity Package Manager cannot resolve the UniTask Git dependency from this
package manifest. Install it before ActionSystem; otherwise Unity compilation
fails by design rather than silently disabling ActionSystem features.

Odin Inspector and Serializer are not required.

## Install

Add this Git URL in Unity Package Manager:

```
https://github.com/HaruFamily/HaruKit.git?path=/Framework/ActionSystem
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.harufamily.framework.actionsystem": "https://github.com/HaruFamily/HaruKit.git?path=/Framework/ActionSystem"
  }
}
```

For reproducible builds, pin the package URL to a release tag or commit.

## Integration

Define the types that give the framework its domain meaning:

1. Create a timing enum and an execution-context type for `TPack`.
2. Implement actions by inheriting `ActionBase<TPack>` and overriding
   `OnExecute`.
3. Implement formulas by inheriting `FormulaBase<TResult, TPack>` and
   overriding `OnEvaluate`.
4. For each formula family, provide a `FormulaAsset<TResult, TPack>` subtype
   and a `FormulaSlot<TResult, TAsset, TFormula, TPack>` subtype.
5. Store `ActionSystem<TTiming, TPack>` on a serializable owner. Implement
   `IActionSystemOwner` on that owner to enable Inspector validation.

```csharp
using Cysharp.Threading.Tasks;
using HaruFamily.Framework.ActionSystem;

[ASNode("Write message")]
public sealed class WriteMessageAction : ActionBase<MyContext>
{
    protected override UniTask OnExecute(MyContext context, TokenTable<MyContext> tokens)
    {
        context.Write("executed");
        return UniTask.CompletedTask;
    }
}

[ASNode("Current value")]
public sealed class CurrentValueFormula : FormulaBase<int, MyContext>
{
    protected override UniTask<int> OnEvaluate(MyContext context, TokenTable<MyContext> tokens)
    {
        return UniTask.FromResult(context.Value);
    }
}
```

`[ASNode]` supplies the graph display name, description, group, and ordering.
The package also provides graph-only presentation attributes such as
`[ASHide]`, `[ASShowIf]`, `[ASLabel]`, `[ASDescription]`, `[ASEnum]`, and
`[ASKind]`.

## Graph Model

An `ActionSlot<TPack>` is an action graph entry point. A
`FormulaSlot<TResult, TAsset, TFormula, TPack>` evaluates a value and always
has a default fallback. Both slots reference a `GraphNode` when they use a
non-constant source.

A graph node has one source at a time:

- **Inline**: an `ActionBase<TPack>` or `FormulaBase<TResult, TPack>` instance
  serialized with the graph.
- **Asset**: a reusable `ActionAssetBase<TPack>` or
  `FormulaAsset<TResult, TPack>` ScriptableObject.
- **Endpoint**: a named formula source defined by `GraphEndpoint`.

Endpoints are typed by their concrete formula-slot type. Names must therefore
be unique within a formula family, while different families may use the same
name. Formula slots resolve endpoint values through `TokenTable<TPack>`, which
also applies asset parameter bindings and prevents recursive resolution.

Asset endpoints form the asset's parameter interface. A node that references
an asset can retain the asset's defaults or provide a constant or graph-based
binding for each endpoint.

## Authoring And Validation

The custom Inspector drawer is the entry point to the graph editor for any
serialized `ActionSystem<,>` field. It can open the graph and, when its owner
implements `IActionSystemOwner`, run validation.

Call `MarkDirty()` after changing a graph through code. In the Editor, call
`Verify()` after authoring changes. A successful validation marks the graph as
executable; `TriggerAction` and `CreateTokenTable` refuse to run an unvalidated
graph. Empty formula slots are valid constant values. Empty enabled action
slots and incompatible or cyclic graph links are validation errors.

The editor also revalidates `IActionSystemOwner` ScriptableObjects when leaving
Edit Mode. Failures are reported in the Console, and invalid graphs remain
blocked from runtime execution.

## Runtime Use

After validation, execute all actions registered for a timing value:

```csharp
await actionSystem.TriggerAction(MyTiming.BeforeExecute, context);
```

Each call creates a fresh `TokenTable<TPack>`. Endpoint values are evaluated on
each request rather than cached, so formulas may safely depend on the current
execution context.

To construct runtime-local state from a shared serialized graph, use:

```csharp
var runtimeActions = definitionActions.DeepCopy();
```

The copy keeps internal sharing and cycles intact while isolating managed graph
state from the original owner.

## Description Compilation

`Compile(template, pack, passes)` creates a `CompileContext<TPack>` and runs
the supplied `ICompilePass<TPack>` instances in order. The framework does not
define template syntax or output formats; consumers provide the passes that
interpret their own templates. Compilation uses the same validated token table
as action execution.

## Package Contents

- `Runtime/Action`: action bases, slots, and reusable action assets
- `Runtime/Formula`: formula bases, slots, and reusable formula assets
- `Runtime/Graph`: graph nodes and shared-reference support
- `Runtime/Token`: named endpoints and token resolution
- `Runtime/Engine`: dispatch, validation, deep copy, and compilation
- `Runtime/Contracts`: editor metadata and owner or asset contracts
- `Editor`: graph editor, Inspector drawer, validation sweep, and formula
  family scaffolding
