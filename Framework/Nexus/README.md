# HaruKit Framework Nexus

Nexus is a scoped async service locator for Unity. It manages service identity,
ownership, lifecycle, pooling, and Addressables-backed prefab and ScriptableObject
services.

## Prerequisites

Install UniTask before installing Nexus. Unity Package Manager package manifests
cannot resolve Git sub-dependencies automatically.

```json
{
  "dependencies": {
    "com.cysharp.unitask": "https://github.com/Cysharp/UniTask.git?path=src/UniTask/Assets/Plugins/UniTask"
  }
}
```

`com.unity.addressables` is declared by this package and resolves through the Unity
registry.

## Install

Add this Git URL in Unity Package Manager:

```
https://github.com/HaruFamily/HaruKit.git?path=/Framework/Nexus
```

Or add it to `Packages/manifest.json`:

```json
{
  "dependencies": {
    "com.harufamily.framework.nexus": "https://github.com/HaruFamily/HaruKit.git?path=/Framework/Nexus"
  }
}
```

For a reproducible release, append a Nexus package tag, for example
`#nexus/v1.0.0`.

## Editor Tools

Use `Tools/Pin/Nexus/Service Tree` to inspect active services. The package includes
its only required Base UI helper internally, so no separate Base package is needed.
