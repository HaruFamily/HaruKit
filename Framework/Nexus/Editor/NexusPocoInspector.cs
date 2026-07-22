#if UNITY_EDITOR
using System;
using System.Reflection;
using PinPlugin.Nexus;
using UnityEditor;
using UnityEngine;

namespace PinPlugin.Nexus.Editor
{
    /// <summary>
    /// 把一個 POCO（純 C# Nexus 服務）包成 <see cref="ScriptableObject"/>，讓**原生 Inspector** 能經
    /// <see cref="NexusPocoProxyEditor"/> 顯示其內容。原生 Inspector 只吃 <see cref="UnityEngine.Object"/>，
    /// POCO 無法直接選取——故用此 proxy 當載體。
    /// <para><b>只記節點 id，不持實例參考</b>：custom editor 每次重繪由 id 重抓 live（反映即時值；服務釋放後顯示「已釋放」，
    /// 且不會被 editor 欄位釘住而無法 GC）。proxy 本身 <c>HideFlags.DontSave</c>，由開啟它的視窗負責 DestroyImmediate。</para>
    /// </summary>
    public sealed class NexusPocoProxy : ScriptableObject
    {
        /// <summary>目標 Nexus 節點 id（0 = 未綁定）。</summary>
        public int Id;

        public void Bind(int id, string title)
        {
            Id = id;
            name = title;   // Inspector 標題顯示型別名 + #Id
        }
    }

    /// <summary>POCO Inspector：反射顯示 public instance members，不依賴第三方 Inspector。</summary>
    [CustomEditor(typeof(NexusPocoProxy))]
    public sealed class NexusPocoProxyEditor : UnityEditor.Editor
    {
        public override bool RequiresConstantRepaint() => true;

        public override void OnInspectorGUI()
        {
            var proxy = (NexusPocoProxy)target;
            if (proxy.Id == 0)
            {
                EditorGUILayout.HelpBox("此 proxy 未綁定任何 Nexus 服務。", MessageType.Info);
                return;
            }

            // 每次重繪重抓 live：服務若已釋放（回 null）就顯示提示，不持參考。
            var inst = Nexus.Instance.ById<object>(proxy.Id);
            if (inst == null)
            {
                EditorGUILayout.HelpBox($"服務 #{proxy.Id} 已釋放或不存在。", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField(inst.GetType().FullName, EditorStyles.miniLabel);
            EditorGUILayout.Space(2);
            EditorGUILayout.HelpBox("唯讀檢視 public 欄位與屬性。", MessageType.None);

            const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public;
            foreach (var field in inst.GetType().GetFields(Flags))
                DrawValue(field.Name, () => field.GetValue(inst));

            foreach (var property in inst.GetType().GetProperties(Flags))
            {
                if (!property.CanRead || property.GetIndexParameters().Length != 0) continue;
                DrawValue(property.Name, () => property.GetValue(inst));
            }
        }

        private static void DrawValue(string name, Func<object> getter)
        {
            try
            {
                var value = getter();
                EditorGUILayout.LabelField(name, value?.ToString() ?? "null");
            }
            catch (Exception exception)
            {
                EditorGUILayout.LabelField(name, $"<{exception.GetType().Name}>");
            }
        }
    }
}
#endif
