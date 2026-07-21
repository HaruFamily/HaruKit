#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace PinTools.Inspector
{
    internal static class InspectorSettingsProvider
    {
        public const string SettingGroupNormal = "一般設定";
        public const string SettingGroupNormal_Info = "調整歷史紀錄容量上限。";
        public const string SettingGroupNormal_maxCount = "歷史上限";
        public const string SettingGroupToggle = "啟用開關";
        public const string SettingGroupToggle_Info = "可關閉整個功能、僅關閉 Inspector 工具列，或在 Play Mode 停用。";
        public const string SettingGroupToggle_enableSystem = "啟用整個功能";
        public const string SettingGroupToggle_enableHeader = "Inspector 顯示工具列";
        public const string SettingGroupToggle_disableInPlayMode = "Play Mode 時停用功能";

        [SettingsProvider]
        public static SettingsProvider CreatePinInspectorSettingsProvider()
        {
            var provider = new SettingsProvider("Project/Pin Tools/Pin Inspector", SettingsScope.Project)
            {
                label = "Pin Inspector",

                guiHandler = searchContext =>
                {
                    var data = JSONStorage.Data;

                    EditorGUILayout.Space(10);

                    // ===== 啟用開關 =====
                    EditorGUILayout.LabelField(SettingGroupToggle, EditorStyles.boldLabel);
                    EditorGUILayout.Space(4);
                    EditorGUILayout.HelpBox(SettingGroupToggle_Info, MessageType.Info);
                    EditorGUILayout.Space(6);

                    EditorGUI.BeginChangeCheck();
                    EditorGUILayout.BeginVertical("box");
                    {
                        bool enableSystem = EditorGUILayout.Toggle(SettingGroupToggle_enableSystem, data.enableSystem);
                        using (new EditorGUI.DisabledScope(!enableSystem))
                        {
                            bool enableHeader = EditorGUILayout.Toggle(SettingGroupToggle_enableHeader, data.enableInspectorHeader);
                            bool disableInPlayMode = EditorGUILayout.Toggle(SettingGroupToggle_disableInPlayMode, data.disableInPlayMode);
                            if (EditorGUI.EndChangeCheck())
                            {
                                data.enableSystem = enableSystem;
                                data.enableInspectorHeader = enableHeader;
                                data.disableInPlayMode = disableInPlayMode;
                                Inspector.Save();
                            }
                        }
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.Space(10);

                    // ===== 一般設定 =====
                    EditorGUILayout.LabelField(SettingGroupNormal, EditorStyles.boldLabel);

                    EditorGUILayout.Space(4);

                    EditorGUILayout.HelpBox(SettingGroupNormal_Info, MessageType.Info);

                    EditorGUILayout.Space(6);

                    EditorGUI.BeginChangeCheck();

                    EditorGUILayout.BeginVertical("box");
                    {
                        int maxCount = EditorGUILayout.IntField(SettingGroupNormal_maxCount, data.maxCount);
                        maxCount = Mathf.Clamp(maxCount, 1, 999);

                        if (EditorGUI.EndChangeCheck())
                        {
                            data.maxCount = maxCount;
                            Inspector.Save();
                        }
                    }
                    EditorGUILayout.EndVertical();

                    EditorGUILayout.Space(10);
                }
            };

            return provider;
        }
    }
}
#endif
