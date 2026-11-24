#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace HaruFamily.UX.Bookmarks
{
    // 資料存在 UserSettings/（個人偏好、不進版控），因此設定頁放在 Preferences 而不是 Project Settings。
    internal static class BookmarksSettingsProvider
    {
        public const string SettingsPath = "Preferences/HaruFamily/Bookmarks";
        public const string SettingGroupNormal = "一般設定";
        public const string SettingGroupNormal_Info = "紀錄超過上限時從最舊的開始移除；改小上限會立刻生效。";
        public const string SettingGroupNormal_maxCount = "紀錄上限";
        public const string SettingGroupNormal_mergeDuplicateHistory = "紀錄列表合併重複項目";
        public const string SettingGroupNormal_showItemBadges = "Project／Hierarchy 顯示書籤 ★";
        public const string SettingGroupNormal_showNotifications = "加入／移除書籤時顯示通知";
        public const string SettingGroupToggle = "啟用開關";
        public const string SettingGroupToggle_Info = "可關閉整個功能、僅關閉 Inspector 工具列，或在 Play Mode 停用。";
        public const string SettingGroupToggle_enableSystem = "啟用整個功能";
        public const string SettingGroupToggle_enableHeader = "Inspector 顯示工具列";
        public const string SettingGroupToggle_disableInPlayMode = "Play Mode 時停用功能";

        [SettingsProvider]
        public static SettingsProvider CreateBookmarksSettingsProvider()
        {
            var provider = new SettingsProvider(SettingsPath, SettingsScope.User)
            {
                label = "Bookmarks",
                keywords = new HashSet<string>(new[] { "Bookmarks", "書籤", "紀錄", "History", "Inspector" }),

                guiHandler = searchContext =>
                {
                    var data = BookmarkStorage.Data;
                    if (!string.IsNullOrEmpty(BookmarkStorage.LastError))
                        EditorGUILayout.HelpBox(BookmarkStorage.LastError, MessageType.Error);
                    if (!BookmarkStorage.IsAvailable) return;

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
                                BookmarkService.Save();
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
                        int maxCount = Mathf.Clamp(EditorGUILayout.DelayedIntField(SettingGroupNormal_maxCount, data.maxCount), 1, 999);
                        bool merge = EditorGUILayout.Toggle(SettingGroupNormal_mergeDuplicateHistory, data.mergeDuplicateHistory);
                        bool badges = EditorGUILayout.Toggle(SettingGroupNormal_showItemBadges, data.showItemBadges);
                        bool notifications = EditorGUILayout.Toggle(SettingGroupNormal_showNotifications, data.showNotifications);

                        if (EditorGUI.EndChangeCheck())
                        {
                            data.maxCount = maxCount;
                            data.mergeDuplicateHistory = merge;
                            data.showNotifications = notifications;
                            if (data.showItemBadges != badges)
                            {
                                data.showItemBadges = badges;
                                EditorApplication.RepaintProjectWindow();
                                EditorApplication.RepaintHierarchyWindow();
                            }
                            BookmarkService.TrimHistoryToLimit();
                            BookmarkService.Save();
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
