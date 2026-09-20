# LogicGraph

Current package version: `2.0.0` (Unity 2021.3+).

LogicGraph is a Unity UPM framework for authoring and executing serialized
action graphs. It provides timing-based action dispatch, typed formulas, named
formula tokens, reusable ScriptableObject graphs, validation, deep copying,
and an editor graph interface. It does not define domain types, timing values,
or action behavior.

## Features

- `LogicGraph<TTiming, TPack>` dispatches asynchronous actions for a
  consumer-defined timing enum and execution context.
- `ActionBase<TPack>` performs side effects; `FormulaBase<TResult, TPack>`
  asynchronously evaluates typed values.
- Slots can use a constant fallback, an inline graph node, a reusable asset,
  or, for formulas, a named token.
- `GraphNode` preserves its identity, notes, disabled state, and editor layout
  while its source changes.
- Action and formula assets expose named tokens as parameters. Each asset
  invocation receives an isolated token scope with optional caller bindings.
- Editor validation detects invalid node sources, duplicate timings or token
  names, incompatible bindings, and graph or asset cycles before execution.
- `DeepCopy()` preserves polymorphic `SerializeReference` graphs, shared
  references, cycles, and Unity object references.
- The node editor comes from GraphKit and has no Odin dependency. LogicGraph
  provides the Inspector entry, optional usage rules, and owner-aware validation.
- All timing groups appear on one canvas. A timing group is one root node whose
  body is an ordered action list, so sources can be shared across timings.

## Requirements

- Unity 2021.3 or later
- [GraphKit](../../DependencyCore/GraphKit):
  `https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit`
- [UniTask](https://github.com/Cysharp/UniTask):
  `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`

Unity Package Manager cannot resolve either Git dependency from this package
manifest. Install both before LogicGraph; otherwise Unity compilation fails by
design rather than silently disabling LogicGraph features.

GraphKit holds the graph carrier, editor contracts, metadata attributes, deep
copy, and node editor. LogicGraph adds timing dispatch, asynchronous execution,
and generic action/formula slots and assets. The namespaces are intentionally
separate:

```csharp
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;
```

Odin Inspector and Serializer are not required.

## Install

Add this Git URL in Unity Package Manager:

```
https://github.com/HaruFamily/HaruKit.git?path=/Framework/LogicGraph
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask",
    "com.harufamily.dependencycore.graphkit": "https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit",
    "com.harufamily.framework.logicgraph": "https://github.com/HaruFamily/HaruKit.git?path=/Framework/LogicGraph"
  }
}
```

For reproducible builds, pin the package URL to a release tag or commit.

## Choose An Extension Point

| Goal | Start here | What you need to know |
|---|---|---|
| Add a side effect | `ActionBase<TPack>.OnExecute` | Pack, child Slots, cancellation |
| Add a calculation to an existing family | That family's Formula base and `OnEvaluate` | Result type and child Slots |
| Add a formula family | Formula / FormulaAsset / Slot; `PinTools/LogicGraph/Add Formula Type` | Family identity is the concrete Slot type, not just the result type |
| Integrate a graph into your game | [Complete first example](#complete-first-example) | Owner, validation, runtime copy, trigger |
| Restrict timings or add domain validation | `ILogicGraphUsage<TTiming, TPack>` | [Optional usage rules](#start-with-one-field) |
| Draw a custom value type | GraphKit's `HGValueDrawer<T>` | Editor-only drawer and explicit Tool context |
| Describe custom node fields | GraphKit's `IHGEditorMetadataProvider` | Complete node descriptor and explicit Tool context |

Ordinary Action/Formula extensions do not need custom Ports, document sessions,
or editor bindings. Those belong to advanced GraphKit tool integration; see the
[GraphKit package](../../DependencyCore/GraphKit).

## Complete First Example

This example logs a base amount supplied by the caller plus a graph-authored
bonus. It defines every type it uses: one timing, one Pack, one formula family,
one Formula, one Action, an asset owner, and a scene runner.

After installing the dependencies, place the following four files under a
runtime folder in `Assets` (not `Editor`). If using an asmdef, reference
`HaruFamily.Framework.LogicGraph`, `HaruFamily.DependencyCore.GraphKit`, and
`UniTask`. These scripts do not need an Editor assembly reference. The snippets
are source to copy into your project, not an automatically installed sample.

### 1. Context and formula family — `DemoIntAsset.cs`

```csharp
using System;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;

namespace LogicGraphQuickStart
{
    public enum DemoTiming { Activate }

    public sealed class DemoPack
    {
        public int BaseAmount;
    }

    public abstract class DemoIntFormula : FormulaBase<int, DemoPack> { }

    public sealed class DemoIntAsset : FormulaAsset<int, DemoPack> { }

    [Serializable]
    [HGKind("Demo Int")]
    public sealed class DemoIntSlot
        : FormulaSlot<int, DemoIntAsset, DemoIntFormula, DemoPack>
    {
        public DemoIntSlot() { }
        public DemoIntSlot(int value) : base(value) { }
    }
}
```

The Pack supplies the current execution context. It is passed by the caller,
not authored into the graph. TokenTable handles named formula lookup, asset
parameter scopes, and execution cancellation/observation.

The three family types have separate jobs: Formula is the calculation base,
FormulaAsset supports reusable graph assets, and Slot is the authored input
and family identity. You do not need to create a `DemoIntAsset` instance for
this example. For later calculations in this family, inherit `DemoIntFormula`;
do not generate another family. The editor discovers concrete Slot families
without a separate registration call.

### 2. Nodes — `DemoNodes.cs`

```csharp
using System;
using Cysharp.Threading.Tasks;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace LogicGraphQuickStart
{
    [Serializable]
    [HGNode("Base plus bonus", "Adds an authored bonus to the caller's base amount.", "Quick Start")]
    public sealed class BasePlusBonusFormula : DemoIntFormula
    {
        [HGLabel("Bonus")]
        public DemoIntSlot Bonus = new DemoIntSlot(2);

        protected override async UniTask<int> OnEvaluate(
            DemoPack pack, TokenTable<DemoPack> tokens)
        {
            return pack.BaseAmount + await Bonus.Evaluate(pack, tokens);
        }
    }

    [Serializable]
    [HGNode("Log amount", "Evaluates Amount and writes it to the Unity Console.", "Quick Start")]
    public sealed class LogAmountAction : ActionBase<DemoPack>
    {
        [HGLabel("Amount")]
        public DemoIntSlot Amount = new DemoIntSlot(1);

        protected override async UniTask OnExecute(
            DemoPack pack, TokenTable<DemoPack> tokens)
        {
            int amount = await Amount.Evaluate(pack, tokens);
            Debug.Log($"LogicGraph amount: {amount}");
        }
    }
}
```

Use a normal serialized field for a fixed setting; use a Slot when the graph
should be able to supply a constant, calculation, reusable asset, or Token.
Always pass the received `pack` and `tokens` into child Slots. Slots apply
disabled-state handling, fallback values, scopes, and execution observation.
For nested actions, use `ActionSlot<DemoPack>.Execute(pack, tokens)`.
Pass `tokens.CancellationToken` to any long-running asynchronous work you add.

### 3. Shared template owner — `DemoGraphDefinition.cs`

```csharp
using HaruFamily.Framework.LogicGraph;
using UnityEngine;

namespace LogicGraphQuickStart
{
    [CreateAssetMenu(menuName = "LogicGraph Quick Start/Definition")]
    public sealed class DemoGraphDefinition : ScriptableObject
    {
        [SerializeField] private LogicGraph<DemoTiming, DemoPack> graph = new();

        public LogicGraph<DemoTiming, DemoPack> CreateRuntimeGraph()
            => graph.DeepCopy();
    }
}
```

No owner interface is required. DeepCopy preserves validation state and internal
managed sharing while isolating the runtime graph from the template. Unity
Object references, including referenced graph assets, remain shared; the copy
does not validate an invalid template or duplicate those assets.

### 4. Trigger from your game — `DemoGraphRunner.cs`

```csharp
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace LogicGraphQuickStart
{
    public sealed class DemoGraphRunner : MonoBehaviour
    {
        [SerializeField] private DemoGraphDefinition definition;

        private async void Start()
        {
            if (definition == null)
            {
                Debug.LogError("Assign a DemoGraphDefinition.", this);
                return;
            }

            var runtimeGraph = definition.CreateRuntimeGraph();
            var pack = new DemoPack { BaseAmount = 10 };
            var cancellation = this.GetCancellationTokenOnDestroy();
            try
            {
                await runtimeGraph.TriggerAction(
                    DemoTiming.Activate, pack, cancellation, "Quick Start");
            }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
            {
                // This runner's lifetime ended; stop its execution chain.
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
            }
        }
    }
}
```

`Start` is the Unity event boundary; ordinary game methods should return and
await `UniTask`. Each trigger creates a fresh TokenTable and awaits actions in
list order. Choose the cancellation lifetime appropriate to your system; this
example uses destruction of the runner.

### 5. Author, validate, and run

1. Create an asset via **Create → LogicGraph Quick Start → Definition**.
2. Open its graph from the Inspector and add the `Activate` timing.
3. Add an action item and assign **Log amount** as its source.
4. Assign **Base plus bonus** to that action's `Amount` Slot; leave `Bonus` at 2.
5. Save the graph successfully; use the Inspector's **Validate** button to
   confirm the stored graph is validated. Resolve any reported errors first.
6. Add `DemoGraphRunner` to a scene GameObject and assign the definition.
7. Enter Play Mode. The expected Console message is **LogicGraph amount: 12**.

With `Amount` unconnected, the result is its constant 1. With the formula
connected, it is the caller's 10 plus the Bonus constant 2. To add another
calculation, only add another serializable `DemoIntFormula` subclass with
`[HGNode]` and `OnEvaluate`.

If no message appears, check the definition assignment, `Activate` action
connection/enabled state, and validation errors. An unvalidated graph logs an
error and skips execution; runtime does not repair validation. If editing the
template after a runner has started, restart Play Mode to create a new copy.

### Rules to keep nearby

- After programmatic authoring changes, call `MarkDirty()` and then validate
  through the Inspector or Editor-only `LogicGraphEditor.Verify(owner)`.
  Do not use `MarkValidated()` to bypass authoring errors.
- A Token is a named calculation, not a cached variable. Repeated requests
  evaluate again, including random formulas. Query with the concrete Slot type:
  `await tokens.Resolve<int>(typeof(DemoIntSlot), "Amount", pack)` requires a
  separately authored Token named `Amount` in that family; it does not refer to
  the action's field of the same name.
- Missing/disabled formula sources use fallback values. Exceptions thrown by
  node bodies still propagate; a fallback is not a general exception handler.
- Keep per-execution context in Pack rather than treating serialized node
  fields as a cross-call cache.

## Integration

### Start with one field

With your domain's timing enum and Pack type, an owner only needs a serialized
graph field. No owner interface or Dirty/Verify forwarding methods are required:

```csharp
using HaruFamily.Framework.LogicGraph;
using UnityEngine;

public sealed class SkillDefinition : ScriptableObject
{
    [SerializeField] private LogicGraph<MyTiming, MyContext> graph = new();
}
```

Use the Inspector's Open and Validate buttons. All values of `MyTiming` are
available by default. For additional rules, implement one optional interface:

```csharp
public sealed class RestrictedSkillDefinition : ScriptableObject,
    ILogicGraphUsage<MyTiming, MyContext>
{
    [SerializeField] private LogicGraph<MyTiming, MyContext> graph = new();
    [SerializeField] private string description;

#if UNITY_EDITOR
    public void ConfigureGraph(LogicGraphUsage<MyTiming, MyContext> usage)
    {
        usage.AllowTimings(new[] { MyTiming.BeforeExecute });
        usage.RequireToken("Damage", nameof(description));
    }
#endif
}
```

`AllowTimings` controls both the creation menu and validation. Null means all
timings; an empty collection permits none. `RequireToken`/`RequireTokens` declare
external references. A project can wrap its description parser in an extension
method rather than repeat token extraction on every owner.

For custom checks, use `usage.AddValidation((graph, report) => ...)` and
`report.Error(code, message, fieldPath)` or `report.Warning(...)`. The callback
receives the document being validated, including an isolated working copy.
Configuration and validation must not mutate the owner, graph, or assets. They
are evaluated on demand, not serialized into the graph.

Rules apply to all documents with the same Timing/Pack types on that owner.
Multiple graph fields must be opened through their Inspector button or an
explicit binding. Automatic discovery covers direct serialized fields,
including inherited private fields, and excludes nonserialized runtime caches.

### Optional execution observation

LogicGraph implements GraphKit's `IGraphExecutionDocument`. `DeepCopy()` preserves
the shared observation source and content revision without sharing execution
state. Each observed `TriggerAction` call creates one independent session. The
Action/Formula Slot entries await Hold before executing node content; token and
asset scopes carry the session through nested evaluations. Asset roots have
their own visits under `asset:<instance id>`, while bindings evaluate in the
caller's scope. Standalone token-table queries outside `TriggerAction` do not
automatically create a timing execution session.

The existing two-argument `TriggerAction` remains available. Call the overload
`TriggerAction(timing, pack, cancellationToken, executionName)` to provide a
lifetime and a readable execution label. On retirement, `CancelObservedExecutions()`
cancels observed sessions of **that graph instance**, not other runtime copies.
Cancellation propagates to the awaiting caller; callers must unwind their own
work. Node bodies should pass `tokens.CancellationToken` to their long-running
asynchronous operations. Observation never changes global time or freezes the
world. Disabled nodes are not entered or held.

Observation fields are nonserialized. `MarkDirty()` advances the document's
observation revision; existing runtime copies retain their prior revision, so
the editor can reject a mismatched live projection. No execution sessions are
recorded when the source has no observers.

### Domain types

Define the types that give the framework its domain meaning:

1. Create a timing enum and an execution-context type for `TPack`.
2. Implement actions by inheriting `ActionBase<TPack>` and overriding
   `OnExecute`.
3. Implement formulas by inheriting `FormulaBase<TResult, TPack>` and
   overriding `OnEvaluate`.
4. For each formula family, provide a `FormulaAsset<TResult, TPack>` subtype
   and a `FormulaSlot<TResult, TAsset, TFormula, TPack>` subtype.
5. Store `LogicGraph<TTiming, TPack>` in a serialized owner field. Add
   `ILogicGraphUsage<TTiming, TPack>` only when custom rules are needed.

See the [complete first example](#complete-first-example) for serializable
Action/Formula implementations, family types, and a caller using child Slots.
The `MyTiming`/`MyContext` names in the integration snippets stand for your own
domain types; the complete example supplies `DemoTiming`/`DemoPack` instead.

`[HGNode]` supplies the graph display name, description, group, and ordering.
The package also provides graph-only presentation attributes such as
`[HGHide]`, `[HGShowIf]`, `[HGLabel]`, `[HGDescription]`, `[HGEnum]`, and
`[HGKind]`.

## Graph Model

An `ActionTimingGroup<TTiming, TPack>` is a root node containing an ordered list
of `ActionSlot<TPack>` values. An `ActionSlot<TPack>` is an action graph entry
point. A
`FormulaSlot<TResult, TAsset, TFormula, TPack>` evaluates a value and always
has a default fallback. Both slots reference a `GraphNode` when they use a
non-constant source.

A graph node has one source at a time:

- **Inline**: an `ActionBase<TPack>` or `FormulaBase<TResult, TPack>` instance
  serialized with the graph.
- **Asset**: a reusable `ActionAssetBase<TPack>` or
  `FormulaAsset<TResult, TPack>` ScriptableObject.
- **Token**: a named formula source defined by `GraphToken`.

Tokens are typed by their concrete formula-slot type. Names must therefore
be unique within a formula family, while different families may use the same
name. Formula slots resolve token values through `TokenTable<TPack>`, which
also applies asset parameter bindings and prevents recursive resolution.

The formula family is the concrete Slot type, not only `TResult`. Two Slot
types that both return `string` remain separate families and may each define a
Token with the same name. Token values are evaluated on every request; they are
not memoized.

Asset tokens form the asset's parameter interface. A node that references
an asset can retain the asset's defaults or provide a constant or graph-based
binding for each token.

## Authoring And Validation

The custom Inspector drawer is the entry point to the graph editor for any
serialized `LogicGraph<,>` field. It opens and validates the selected field.
`PinTools/LogicGraph/開啟節點圖` and `Assets/LogicGraph/開啟節點圖` open a single-document
owner. Usage rules also work with GraphKit's default context.

Call `MarkDirty()` after changing a graph through code. In the Editor, call
`LogicGraphEditor.Verify(owner)` from the LogicGraph Editor namespace after
authoring changes, or use the Inspector. The parameterless `graph.Verify()`
checks only the core graph rules; it has no owner from which to obtain Usage. A
successful validation marks the graph as executable; `TriggerAction` and
`CreateTokenTable` refuse to run an unvalidated graph. Empty formula slots are
valid constant values. Empty enabled action slots, duplicate timing groups,
invalid Token endpoints, incompatible asset bindings, and graph or asset cycles
are validation errors. Incomplete content reachable only through a disabled
node is reported as a warning.

The editor also discovers serialized LogicGraph fields on ScriptableObject assets
and revalidates them when leaving Edit Mode. No owner-interface opt-in is needed. Failures are
reported in the Console; this sweep does not block entering Play Mode, but
invalid graphs remain blocked from runtime execution.

## Runtime Use

After validation, execute all actions registered for a timing value:

```csharp
await actionSystem.TriggerAction(MyTiming.BeforeExecute, context);
```

Each call creates a fresh `TokenTable<TPack>`. Token values are evaluated on
each request rather than cached, so formulas may safely depend on the current
execution context. Disabled or incompatible formula sources return the Slot's
default value; disabled or incompatible actions are skipped.

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

- `Runtime/Action`: action bases, generic action slot, reusable action assets
- `Runtime/Formula`: formula bases, generic formula slot, reusable formula
  assets
- `Runtime/Token`: token resolution
- `Runtime/Engine`: dispatch, validation, and compilation
- `Runtime/Engine/LogicGraphUsage.cs`: optional content rules and validation reports
- `Editor`: explicit entry, Inspector drawer, validation sweep, and formula family scaffolding

The graph carrier, the editor contracts, the `[HG*]` attributes, deep copy, and
the node editor window all live in GraphKit.

## Tests

- `GraphDeepCopyTests`: copying, Token reevaluation, bindings, and formula fallbacks.
- `LogicGraphExecutionTests`: execution observation, Hold, cancellation, and author API boundaries.
- `LogicGraphValidationTests`: structured diagnostics and legacy owner integration.
- `LogicGraphUsageTests`: interface-free owners, usage rules, selected-field and batch
  validation, default-context commits, and shared-asset revalidation.

Run these as EditMode tests in Unity Test Runner.
