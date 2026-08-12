namespace PinPlugin.ActionSystem
{
#if UNITY_EDITOR
using System;
using UnityEngine;

/// <summary>
/// Runtime → Editor 的單向掛勾。Editor assembly 載入時填入實作，Core 不反過來引用 Editor。
/// </summary>
// Editor asmdef 引用 Runtime，反向引用會circular；Inspector 上的按鈕住在 Core，只好留一個委派讓 Editor 端接上。
public static class ActionSystemEditorHooks
{
    /// <summary>開啟視覺化編輯器並聚焦到指定 Owner。由 ActionGraphWindow 在載入時註冊。</summary>
    public static Action<ScriptableObject> OpenGraphWindow;

    public static bool CanOpenGraph => OpenGraphWindow != null;
}
#endif

}
