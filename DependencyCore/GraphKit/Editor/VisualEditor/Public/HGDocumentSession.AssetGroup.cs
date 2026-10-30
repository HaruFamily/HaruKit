namespace HaruFamily.DependencyCore.GraphKit.Editor
{
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class HGAssetGroupResult
{
    public bool Success { get; internal set; }
    public bool RollbackSucceeded { get; internal set; }
    public string Diagnostic { get; internal set; }
    public string RecoveryDirectory { get; internal set; }
}

public sealed partial class HGDocumentSession<TDocument> where TDocument : class, IGraphDocument
{
    private bool assetGroupActive;
    private bool assetGroupClosed;
    /// <summary>Explicit multi-file transaction. Checks every guard before writing, invalidates all documents,
    /// validates staged references, saves only selected files, and restores file/memory backups on failure.
    /// Sessions must be reopened afterwards. Not crash-atomic; durable backups survive an interrupted save.</summary>
    public static HGAssetGroupResult CommitAssetsPreservingOwners(IReadOnlyList<HGDocumentSession<TDocument>> sessions)
    {
        var result = new HGAssetGroupResult();
        var staged = new Dictionary<HGDocumentSession<TDocument>, TDocument>();
        var memory = new Dictionary<UnityEngine.Object, string>();
        var disks = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        bool writing = false;
        try
        {
            if (sessions == null || sessions.Count == 0 || sessions.Count > 16 || sessions.Distinct().Count() != sessions.Count)
                throw new ArgumentException("Provide 1..16 distinct sessions.");
            if (sessions.Select(session => (session.Owner, session.DocumentId)).Distinct().Count() != sessions.Count)
                throw new ArgumentException("A document may occur only once in a transaction.");
            foreach (var session in sessions)
            {
                if (session.atomicActive || session.assetGroupActive || session.assetGroupClosed || session.Owner == null || !AssetDatabase.Contains(session.Owner))
                    throw new InvalidOperationException("Only persistent idle asset sessions can join a transaction.");
                var problem = session.commitGuard.Check();
                if (problem != null) throw new InvalidOperationException(problem.Message);
                string path = AssetDatabase.GetAssetPath(session.Owner);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsOpenForEdit(path))
                    throw new InvalidOperationException("Transaction assets must be editable files under Assets.");
                if (!disks.ContainsKey(path))
                {
                    if (new FileInfo(path).Length > 32 * 1024 * 1024) throw new InvalidOperationException("Asset exceeds 32 MiB.");
                    disks.Add(path, File.ReadAllBytes(path));
                    if (disks.Values.Sum(bytes => (long)bytes.Length) > 128L * 1024 * 1024)
                        throw new InvalidOperationException("Combined asset backups exceed 128 MiB.");
                    foreach (var owner in AssetDatabase.LoadAllAssetsAtPath(path).Where(value => value != null))
                    {
                        if (EditorUtility.IsDirty(owner) || SerializationUtility.HasManagedReferencesWithMissingTypes(owner))
                            throw new InvalidOperationException("Dirty or missing-type Owner cannot join a transaction.");
                        memory.Add(owner, EditorJsonUtility.ToJson(owner));
                    }
                }
                if (!session.TryClone(session.Document, out TDocument copy) || HGMissingTypeCleanup.HasLostContent(copy))
                    throw new InvalidOperationException("Document cannot be completely copied.");
                copy.InvalidateValidation();
                staged.Add(session, copy);
            }
            // Write backup files and the path map before changing any Owner.
            string recovery = Path.GetFullPath(Path.Combine("Library", "GraphKitAssetRecovery", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(recovery);
            result.RecoveryDirectory = recovery;
            var paths = disks.Keys.OrderBy(path => path, StringComparer.Ordinal).ToArray();
            for (int i = 0; i < paths.Length; i++) File.WriteAllBytes(Path.Combine(recovery, i + ".before"), disks[paths[i]]);
            File.WriteAllLines(Path.Combine(recovery, "paths.txt"), paths);
            File.WriteAllText(Path.Combine(recovery, "state.txt"), "prepared");
            writing = true;
            foreach (var session in sessions) session.assetGroupActive = true;
            foreach (var pair in staged)
            {
                if (!pair.Key.binding.TryWrite(pair.Key.Owner, pair.Value)
                    || !pair.Key.binding.TryRead(pair.Key.Owner, out var stored) || !ReferenceEquals(stored, pair.Value))
                    throw new InvalidOperationException("A binding did not retain its staged document.");
                EditorUtility.SetDirty(pair.Key.Owner);
            }
            foreach (var session in sessions)
            {
                var errors = session.ValidateStructure().Where(item => item.Severity == GraphDiagnosticSeverity.Error).Take(8).ToArray();
                if (errors.Length > 0) throw new InvalidOperationException("Staged document " + session.DocumentId + " failed validation: "
                    + string.Join("; ", errors.Select(item => item.Code + ": " + (item.Message?.Length > 256 ? item.Message.Substring(0, 256) : item.Message))));
            }
            File.WriteAllText(Path.Combine(recovery, "state.txt"), "saving");
            foreach (string path in paths)
            {
                var owner = sessions.First(session => AssetDatabase.GetAssetPath(session.Owner) == path).Owner;
                AssetDatabase.SaveAssetIfDirty(owner);
                if (AssetDatabase.LoadAllAssetsAtPath(path).Any(value => value != null && EditorUtility.IsDirty(value)))
                    throw new InvalidOperationException("Asset remains dirty after save: " + path);
            }
            File.WriteAllText(Path.Combine(recovery, "state.txt"), "committed");
            result.Success = true;
            return result;
        }
        catch (Exception exception)
        {
            result.Diagnostic = exception.Message;
            if (!writing) { result.RollbackSucceeded = true; return result; }
            try
            {
                var failures = new List<string>();
                foreach (var pair in disks)
                    try { File.WriteAllBytes(pair.Key, pair.Value); }
                    catch (Exception failure) { failures.Add(pair.Key + ": " + failure.Message); }
                foreach (var pair in memory)
                {
                    try
                    {
                        if (pair.Key == null) throw new InvalidOperationException("Owner disappeared during rollback.");
                        EditorJsonUtility.FromJsonOverwrite(pair.Value, pair.Key);
                        EditorUtility.ClearDirty(pair.Key);
                    }
                    catch (Exception failure) { failures.Add("Owner restore: " + failure.Message); }
                }
                foreach (var pair in disks)
                    try { if (!File.ReadAllBytes(pair.Key).SequenceEqual(pair.Value)) failures.Add("File rollback mismatch: " + pair.Key); }
                    catch (Exception failure) { failures.Add("File verification: " + failure.Message); }
                foreach (var pair in memory)
                    try { if (pair.Key == null || EditorJsonUtility.ToJson(pair.Key) != pair.Value) failures.Add("Memory rollback mismatch."); }
                    catch (Exception failure) { failures.Add("Memory verification: " + failure.Message); }
                if (failures.Count > 0) throw new InvalidOperationException(string.Join("; ", failures));
                result.RollbackSucceeded = true;
                File.WriteAllText(Path.Combine(result.RecoveryDirectory, "state.txt"), "rolled_back");
            }
            catch (Exception rollback) { result.Diagnostic += " Rollback failed: " + rollback.Message; }
            return result;
        }
        finally
        {
            if (writing) foreach (var session in sessions)
            { session.assetGroupActive = false; session.assetGroupClosed = true; }
        }
    }
}
}
