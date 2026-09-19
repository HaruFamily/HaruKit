using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("HaruFamily.Tools.AssetPipeline.Editor.Tests")]

namespace HaruFamily.Tools.AssetPipeline
{
    /// <summary>以資產檔案與原始 meta 為單位的寫入日誌；同一路徑只備份第一次修改前的版本。</summary>
    internal sealed class PipelineAssetTransaction
    {
        [Serializable]
        private sealed class Entry
        {
            public string path;
            public bool existed;
            public string backup;
        }

        [Serializable]
        private sealed class FolderEntry
        {
            public string path;
            public List<string> files = new();
            public List<string> directories = new();
        }

        [Serializable]
        private sealed class Journal
        {
            public bool committed;
            public List<Entry> entries = new();
            public List<FolderEntry> folders = new();
        }

        private readonly Journal journal = new();
        private readonly HashSet<string> captured = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<Object, string> liveSnapshots = new();
        private bool finished;
        public string DirectoryPath { get; } = Path.Combine(JournalRoot, Guid.NewGuid().ToString("N"));
        private static string JournalRoot => Path.GetFullPath("Library/AssetPipelineTransactions");

        public void Capture(string path)
        {
            if (finished) throw new InvalidOperationException("資產交易已結束。");
            path = NormalizePath(path);
            if (captured.Contains(path)) return;
            if (!Directory.Exists(Path.GetDirectoryName(path))) throw new IOException($"目標資料夾不存在：{path}");
            bool generated = journal.folders.Exists(folder => path.StartsWith(folder.path + "/", StringComparison.OrdinalIgnoreCase)
                && !folder.files.Exists(file => string.Equals(file, path, StringComparison.OrdinalIgnoreCase)));
            bool existed = File.Exists(path) && !generated;
            if (existed)
            {
                if (!File.Exists(path + ".meta")) throw new IOException($"資產缺少 meta，無法保存原始識別：{path}");
                foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (asset != null && EditorUtility.IsDirty(asset))
                        throw new InvalidOperationException($"資產有尚未儲存的修改，請先儲存再執行 AP：{path}");
                    if (asset is ScriptableObject && !liveSnapshots.ContainsKey(asset))
                        liveSnapshots.Add(asset, EditorJsonUtility.ToJson(asset));
                }
            }
            else if (!generated && File.Exists(path + ".meta")) throw new IOException($"目標已有孤立 meta：{path}");

            Directory.CreateDirectory(DirectoryPath);
            var entry = new Entry { path = path, existed = existed, backup = journal.entries.Count.ToString("D6") };
            if (existed)
            {
                File.Copy(path, Path.Combine(DirectoryPath, entry.backup + ".data"));
                File.Copy(path + ".meta", Path.Combine(DirectoryPath, entry.backup + ".meta"));
            }
            journal.entries.Add(entry);
            SaveJournal();
            captured.Add(path);
        }

        /// <summary>供會自動命名多份資產的 Editor API 使用；寫入範圍必須事先明確指定。</summary>
        internal void CaptureFolder(string folderPath)
        {
            string path = Path.GetDirectoryName(NormalizePath(folderPath.TrimEnd('/', '\\') + "/__ap_probe__")).Replace('\\', '/');
            if (!AssetDatabase.IsValidFolder(path)) throw new IOException($"產出範圍不存在：{path}");
            if (journal.folders.Exists(folder => string.Equals(folder.path, path, StringComparison.OrdinalIgnoreCase))) return;
            var entry = new FolderEntry { path = path };
            foreach (string file in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                string normalized = file.Replace('\\', '/');
                entry.files.Add(normalized);
                if (!normalized.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) Capture(normalized);
            }
            foreach (string directory in Directory.GetDirectories(path, "*", SearchOption.AllDirectories))
                entry.directories.Add(directory.Replace('\\', '/'));
            Directory.CreateDirectory(DirectoryPath);
            journal.folders.Add(entry);
            SaveJournal();
        }

        public void Commit()
        {
            if (finished) throw new InvalidOperationException("資產交易已結束。");
            journal.committed = true;
            if (Directory.Exists(DirectoryPath)) SaveJournal();
            finished = true;
            TryRemoveJournal(DirectoryPath);
        }

        public List<string> Rollback()
        {
            if (finished) return new List<string>();
            var errors = Restore(DirectoryPath, journal, () =>
            {
                // 同一個 Editor session 內也還原已載入的設定與其反序列化快取。
                // JSON 只存於記憶體，跨 Domain Reload 回復一律由磁碟重新匯入，避免沿用舊 instance id。
                foreach (var snapshot in liveSnapshots)
                {
                    if (snapshot.Key == null) continue;
                    EditorJsonUtility.FromJsonOverwrite(snapshot.Value, snapshot.Key);
                    if (snapshot.Key is ISerializationCallbackReceiver receiver) receiver.OnAfterDeserialize();
                    EditorUtility.ClearDirty(snapshot.Key);
                }
            });
            finished = errors.Count == 0;
            return errors;
        }

        /// <summary>失敗日誌跨 Domain Reload 保留；未完成回復前不得開下一次寫入交易。</summary>
        public static List<string> PendingRecoveryDirectories()
        {
            var result = new List<string>();
            if (!Directory.Exists(JournalRoot)) return result;
            foreach (string directory in Directory.GetDirectories(JournalRoot))
            {
                string file = Path.Combine(directory, "journal.json");
                if (!File.Exists(file)) continue;
                try
                {
                    if (!JsonUtility.FromJson<Journal>(File.ReadAllText(file)).committed) result.Add(directory);
                }
                catch { result.Add(directory); }
            }
            return result;
        }

        public static List<string> Recover(string directory)
        {
            try
            {
                string full = Path.GetFullPath(directory);
                if (!full.StartsWith(JournalRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("不是 AP 的回復日誌位置。");
                var saved = JsonUtility.FromJson<Journal>(File.ReadAllText(Path.Combine(full, "journal.json")));
                if (saved == null || saved.committed) throw new InvalidOperationException("日誌無效或交易已提交。");
                return Restore(full, saved);
            }
            catch (Exception exception) { return new List<string> { $"回復失敗：{directory}\n{exception.Message}" }; }
        }

        private static List<string> Restore(string directory, Journal saved, Action restoreMemory = null)
        {
            var errors = new List<string>();
            if (saved.entries.Count == 0 && saved.folders.Count == 0) { TryRemoveJournal(directory); return errors; }
            foreach (Entry entry in saved.entries)
                if (entry.existed && (!File.Exists(Path.Combine(directory, entry.backup + ".data"))
                    || !File.Exists(Path.Combine(directory, entry.backup + ".meta"))))
                    errors.Add($"{entry.path}：缺少原始備份，未開始回復。");
            if (errors.Count > 0) return errors;
            AssetDatabase.ReleaseCachedFileHandles();
            foreach (FolderEntry folder in saved.folders)
            {
                try
                {
                    string root = Path.GetDirectoryName(NormalizePath(folder.path + "/__ap_probe__")).Replace('\\', '/');
                    var originalFiles = new HashSet<string>(folder.files, StringComparer.OrdinalIgnoreCase);
                    foreach (string file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
                        if (!originalFiles.Contains(file.Replace('\\', '/'))) File.Delete(file);
                    var originalDirectories = new HashSet<string>(folder.directories, StringComparer.OrdinalIgnoreCase);
                    var directories = new List<string>(Directory.GetDirectories(root, "*", SearchOption.AllDirectories));
                    directories.Sort((a, b) => b.Length.CompareTo(a.Length));
                    foreach (string item in directories)
                        if (!originalDirectories.Contains(item.Replace('\\', '/'))) Directory.Delete(item, false);
                }
                catch (Exception exception) { errors.Add($"{folder.path}：無法回復產出範圍：{exception.Message}"); }
            }
            for (int i = saved.entries.Count - 1; i >= 0; i--)
            {
                Entry entry = saved.entries[i];
                try
                {
                    string path = NormalizePath(entry.path);
                    if (entry.existed)
                    {
                        string data = Path.Combine(directory, entry.backup + ".data");
                        string meta = Path.Combine(directory, entry.backup + ".meta");
                        if (!File.Exists(data) || !File.Exists(meta)) throw new IOException("缺少原始檔案備份。");
                        File.Copy(data, path, true);
                        File.Copy(meta, path + ".meta", true);
                    }
                    else
                    {
                        if (File.Exists(path)) File.Delete(path);
                        if (File.Exists(path + ".meta")) File.Delete(path + ".meta");
                    }
                }
                catch (Exception exception) { errors.Add($"{entry.path}：{exception.Message}"); }
            }
            // 重新匯入讓 Prefab／ScriptableObject 的記憶體內容與磁碟回復結果一致。
            Application.LogCallback importError = (message, stack, kind) =>
            {
                if (kind is LogType.Error or LogType.Exception or LogType.Assert) errors.Add("回復匯入：" + message);
            };
            Application.logMessageReceived += importError;
            try
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                foreach (Entry entry in saved.entries)
                    if (entry.existed && File.Exists(entry.path))
                        AssetDatabase.ImportAsset(entry.path, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                restoreMemory?.Invoke();
            }
            catch (Exception exception) { errors.Add($"重新匯入失敗：{exception.Message}"); }
            finally { Application.logMessageReceived -= importError; }
            if (errors.Count == 0)
            {
                try
                {
                    // 先標記交易已結束；清理資料夾失敗時不得讓下次再次套用舊備份。
                    saved.committed = true;
                    WriteJournal(directory, saved);
                    TryRemoveJournal(directory);
                }
                catch (Exception exception) { errors.Add($"回復完成但日誌結案失敗：{exception.Message}"); }
            }
            return errors;
        }

        private void SaveJournal() => WriteJournal(DirectoryPath, journal);

        private static void WriteJournal(string directory, Journal value)
        {
            string path = Path.Combine(directory, "journal.json");
            string temporary = path + ".tmp";
            File.WriteAllText(temporary, JsonUtility.ToJson(value, true));
            if (File.Exists(path)) File.Replace(temporary, path, null);
            else File.Move(temporary, path);
        }

        private static void TryRemoveJournal(string directory)
        {
            if (!Directory.Exists(directory)) return;
            try { Directory.Delete(directory, true); }
            catch (Exception exception) { Debug.LogWarning($"[AssetPipeline] 日誌清理失敗：{directory}\n{exception.Message}"); }
        }

        internal static string NormalizePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("資產路徑為空。");
            string full = Path.GetFullPath(path);
            string root = Path.GetFullPath(Application.dataPath) + Path.DirectorySeparatorChar;
            if (!full.StartsWith(root, StringComparison.OrdinalIgnoreCase) || full.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)
                || Directory.Exists(full)) throw new ArgumentException($"只接受 Assets 內的資產檔案：{path}");
            return "Assets/" + full.Substring(root.Length).Replace('\\', '/');
        }
    }

    /// <summary>每一步共用同一份交易，結果歸屬該步；Action 不需撰寫反向操作。</summary>
    public sealed class PipelineAssetWriter
    {
        private readonly PipelineAssetTransaction transaction;
        private readonly PipelineActionResult result;
        internal PipelineAssetWriter(PipelineAssetTransaction transaction, PipelineActionResult result)
        {
            this.transaction = transaction;
            this.result = result;
        }

        public GameObject CopyPrefab(string sourcePath, string targetPath)
            => WithAssetPath(targetPath, () => CopyPrefabCore(sourcePath, targetPath));

        private GameObject CopyPrefabCore(string sourcePath, string targetPath)
        {
            sourcePath = PipelineAssetTransaction.NormalizePath(sourcePath);
            targetPath = PipelineAssetTransaction.NormalizePath(targetPath);
            if (string.Equals(sourcePath, targetPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("不能以原型自身作為複本目的地。");
            var source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null || !sourcePath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)
                || !targetPath.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"來源／目標必須是 Prefab：{sourcePath} → {targetPath}");
            transaction.Capture(targetPath);
            bool existed = File.Exists(targetPath);
            if (!existed)
            {
                if (!AssetDatabase.CopyAsset(sourcePath, targetPath)) throw new IOException($"複製失敗：{targetPath}");
            }
            else
            {
                // 使用 Unity 的覆寫 API 保存目的地 GUID，不先刪除既有 Prefab。
                GameObject root = PrefabUtility.LoadPrefabContents(sourcePath);
                try
                {
                    PrefabUtility.SaveAsPrefabAsset(root, targetPath, out bool success);
                    if (!success) throw new IOException($"覆寫 Prefab 失敗：{targetPath}");
                }
                finally { PrefabUtility.UnloadPrefabContents(root); }
            }
            AssetDatabase.ImportAsset(targetPath, ImportAssetOptions.ForceSynchronousImport);
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(targetPath);
            if (asset == null) throw new IOException($"無法載入產出：{targetPath}");
            result.Record(existed ? PipelineItemStatus.Modified : PipelineItemStatus.Created, asset);
            return asset;
        }

        public void EditPrefab(string path, Action<GameObject> edit)
            => WithAssetPath(path, () => { EditPrefabCore(path, edit); return true; });

        private void EditPrefabCore(string path, Action<GameObject> edit)
        {
            path = PipelineAssetTransaction.NormalizePath(path);
            if (!path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase)) throw new ArgumentException($"不是 Prefab：{path}");
            transaction.Capture(path);
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            try
            {
                edit(root);
                PrefabUtility.SaveAsPrefabAsset(root, path, out bool success);
                if (!success) throw new IOException($"儲存 Prefab 失敗：{path}");
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            result.Record(PipelineItemStatus.Modified, AssetDatabase.LoadAssetAtPath<GameObject>(path), path);
        }

        /// <summary>整組先登記再修改；適用 Addressables 等跨多份設定的操作，不允許委派寫入未登記路徑。</summary>
        public void EditAssetSet(IEnumerable<Object> storage, Object affectedAsset, Action edit, string message = null)
            => WithAssetPath(AssetDatabase.GetAssetPath(affectedAsset), () =>
            {
                EditAssetSetCore(storage, affectedAsset, edit, message);
                return true;
            });

        private void EditAssetSetCore(IEnumerable<Object> storage, Object affectedAsset, Action edit, string message)
        {
            var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Object asset in storage)
            {
                if (asset == null) continue;
                string path = PipelineAssetTransaction.NormalizePath(AssetDatabase.GetAssetPath(asset));
                transaction.Capture(path);
                paths.Add(path);
            }
            if (paths.Count == 0) throw new InvalidOperationException("沒有登記要修改的資產。");
            edit();
            foreach (string path in paths)
            {
                Object asset = AssetDatabase.LoadMainAssetAtPath(path);
                EditorUtility.SetDirty(asset);
                AssetDatabase.SaveAssetIfDirty(asset);
            }
            result.Record(PipelineItemStatus.Modified, affectedAsset, message: message);
        }

        private static T WithAssetPath<T>(string path, Func<T> operation)
        {
            try { return operation(); }
            catch (Exception exception)
            {
                exception.Data["AssetPipeline.AssetPath"] = path;
                throw;
            }
        }

        /// <summary>包住會自動產生群組／schema 等多個資產的操作；Action／Formula 不用各寫刪除邏輯。</summary>
        public Object CreateAssetSet(IEnumerable<Object> storage, string generatedFolder, Func<Object> create, string message = null)
            => WithAssetPath(generatedFolder, () =>
            {
                foreach (Object asset in storage)
                    if (asset != null) transaction.Capture(AssetDatabase.GetAssetPath(asset));
                transaction.CaptureFolder(generatedFolder);
                Object created = create();
                if (created == null) throw new IOException("建立資產失敗：" + generatedFolder);
                foreach (string file in Directory.GetFiles(generatedFolder, "*", SearchOption.AllDirectories))
                {
                    if (file.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) continue;
                    Object asset = AssetDatabase.LoadMainAssetAtPath(file.Replace('\\', '/'));
                    if (asset != null) AssetDatabase.SaveAssetIfDirty(asset);
                }
                result.Record(PipelineItemStatus.Created, created, message: message);
                return created;
            });
    }
}
