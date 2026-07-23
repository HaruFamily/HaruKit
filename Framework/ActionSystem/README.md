# ActionSystem

Generic ActionSystem for Unity: timing-driven actions, typed formulas, named
tokens, editor validation, shared ScriptableObject formulas/actions, and
description compilation. The package provides infrastructure only. Each game
defines its own timing enum, execution pack, token kinds, formulas, and actions.

## Requirements

Install these dependencies before this package:

- [UniTask](https://github.com/Cysharp/UniTask):
  `https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask`
- Odin Inspector and Serializer 4.0.2 or compatible: required for inspector
  attributes and `SerializationUtility.CreateCopy` graph cloning.

Unity Package Manager cannot resolve the UniTask Git dependency from this
package manifest. Odin is an Asset Store package, so it cannot be declared by
the manifest. Missing either dependency causes Unity compilation to fail by
design rather than silently disabling ActionSystem features.

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

For a reproducible release, append `#actionsystem/v1.0.1` after the path.

## Package Boundary

`PinPlugin.ActionSystem` deliberately does not know gameplay types. Use
`ActionSystem<TTiming, TPack, TTokenEntryPack>` with a project-owned enum,
runtime pack, and token pack. Keep concrete `ActionBase<TPack>`,
`FormulaBase<TResult, TPack>`, token entries, and ScriptableObject subclasses
in the consuming project.

The package contains source code, editor tools, and Unity `.meta` files only.
It does not require prefabs, ScriptableObject assets, Addressables, or project
settings, so Git URL installation has no missing-asset setup step.

## Editor Tools

- `Tools/Pin/ActionSystem/Add Formula Type`: generates a project-owned formula
  kind skeleton.
- Before entering Play mode, `ActionSystemAutoVerifySweep` validates dirty
  `IActionSystemOwner` ScriptableObjects that opt into auto-verification.

## Upgrade From Pre-UPM Source

The package preserves legacy managed-reference data created while the Core was
compiled into `Assembly-CSharp`. `ActionSystem`, `ActionTimingGroup`, and
`ActionSlot` carry `MovedFrom` metadata. After installing, open affected assets,
run validation, confirm their action graphs are present, then save them.
