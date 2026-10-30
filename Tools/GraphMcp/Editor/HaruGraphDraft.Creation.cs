namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.IO;
using System.Linq;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private static Type[] CreatableAssetTypes() => HGTypeIndex.Concrete(typeof(ScriptableObject))
        .Where(type => typeof(IEditableGraphAsset).IsAssignableFrom(type)
            && !HGDocumentInspection.DocumentFields(type).Any())
        .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal).ToArray();

    private static object AssetTypes(ToolParams input)
    {
        int offset = input.GetRaw("offset")?.ToObject<int>() ?? 0;
        int pageSize = input.GetRaw("page_size")?.ToObject<int>() ?? 25;
        if (offset < 0 || pageSize < 1 || pageSize > 100) return Error("invalid_page", "offset >= 0 and page_size 1..100 are required.");
        string search = input.Get("search");
        var types = CreatableAssetTypes().Where(type => string.IsNullOrEmpty(search)
            || type.FullName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        return new SuccessResponse("Registered concrete IEditableGraphAsset types, excluding test assemblies and additional document fields. No constructors invoked. new_asset still checks that the type supplies complete default graph state.", new
        {
            total = types.Length, next_offset = (long)offset + pageSize < types.Length ? (int?)(offset + pageSize) : null,
            entries = types.Skip(offset).Take(pageSize).Select(type => new { type = type.AssemblyQualifiedName, label = HGReflect.TypeName(type) }).ToArray(),
        });
    }

    private static object NewAsset(string typeName)
    {
        if (drafts.Count >= 16) return Error("draft_limit", "Discard a previous draft before creating another (maximum 16).");
        var type = CreatableAssetTypes().SingleOrDefault(candidate => candidate.AssemblyQualifiedName == typeName);
        if (type == null) return Error("asset_type_unavailable", "Select an exact type from asset_types; arbitrary types and test assemblies are not accepted.");
        ScriptableObject owner = null;
        bool retained = false;
        try
        {
            owner = ScriptableObject.CreateInstance(type);
            owner.name = type.Name;
            owner.hideFlags = UnityEngine.HideFlags.HideAndDontSave;
            string id = Guid.NewGuid().ToString("N");
            string revision = TransientRevision(owner);
            var adapter = new HGAssetDocumentBinding(owner, "new:" + id + "/$asset", "new:" + id, TransientRevision);
            var live = adapter.ReadDocument();
            var baseline = HGDocumentInspection.Capture(live, "agent_baseline");
            string hash = Fingerprint(live);
            if (!HGDocumentSession<IGraphDocument>.TryOpenPreservingOwner(owner, adapter.Binding, out var session, out var diagnostic))
                return new ErrorResponse("harugraph.draft.open_failed", new { diagnostic });
            if (TransientRevision(owner) != revision) return Error("source_changed_during_open", "A provider changed the new asset's template while opening; no draft retained.");
            var draft = new Draft { Session = session, OwnsOwner = true, Expires = DateTime.UtcNow.AddMinutes(30),
                ReadDocument = adapter.ReadDocument, BaselineDocument = live, Baseline = baseline,
                BaselineHash = hash, AssetRevision = revision };
            drafts.Add(id, draft);
            retained = true;
            return Status(id, draft);
        }
        finally
        {
            if (!retained && owner != null && !EditorUtility.IsPersistent(owner)) UnityEngine.Object.DestroyImmediate(owner);
        }
    }

    private static string TransientRevision(UnityEngine.Object owner)
    {
        string json = EditorJsonUtility.ToJson(owner);
        if (json.Length > 16 * 1024 * 1024) throw new InvalidOperationException("New asset exceeds the revision size limit.");
        return Hash(System.Text.Encoding.UTF8.GetBytes(json));
    }

    private static string NewAssetBlocker(Draft draft)
    {
        if (EditorUtility.IsPersistent(draft.Session.Owner)) return "The temporary Owner was persisted externally; inspect it and open a new draft.";
        if (!ReferenceEquals(draft.ReadDocument(), draft.BaselineDocument) || TransientRevision(draft.Session.Owner) != draft.AssetRevision)
            return "The temporary source changed outside the draft. Discard and create a new draft.";
        if (SerializationUtility.HasManagedReferencesWithMissingTypes(draft.Session.Owner)) return "New asset has missing managed-reference types.";
        if (draft.Session.Owner is IActionGraphAsset && draft.Session.Document is HGAssetDocument document
            && document.State.Input.Node is GraphNode root && root.Kind != NodeKind.Empty && root.Kind != NodeKind.Inline)
            return "Action asset roots must be inline Action content.";
        return null;
    }

    private static string AbsoluteAssetPath(string path) => Path.GetFullPath(Path.Combine(Application.dataPath, "..", path));

    private static string DestinationBlocker(string path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 220 || !path.StartsWith("Assets/", StringComparison.Ordinal)
            || !path.EndsWith(".asset", StringComparison.Ordinal) || path.Contains('\\'))
            return "Use an exact Assets/.../Name.asset path (forward slashes, at most 220 characters).";
        foreach (string segment in path.Split('/'))
        {
            if (string.IsNullOrWhiteSpace(segment) || segment == "." || segment == ".."
                || segment != segment.Trim() || segment.EndsWith(".", StringComparison.Ordinal)
                || segment.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || segment.StartsWith(".", StringComparison.Ordinal)
                || segment.EndsWith("~", StringComparison.Ordinal))
                return "Destination contains an invalid or hidden path segment.";
            string stem = segment.Split('.')[0].ToUpperInvariant();
            if (new[] { "CON", "PRN", "AUX", "NUL" }.Contains(stem)
                || stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal) || stem.StartsWith("LPT", StringComparison.Ordinal))
                    && stem[3] >= '0' && stem[3] <= '9')
                return "Destination contains a reserved device name.";
        }
        string folder = Path.GetDirectoryName(path).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(folder)) return "Select an existing imported folder; folders are not created automatically.";
        var directory = new DirectoryInfo(AbsoluteAssetPath(folder));
        string assets = Path.GetFullPath(Application.dataPath);
        while (directory != null)
        {
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) return "Linked/junction destination folders are not supported.";
            if (string.Equals(directory.FullName, assets, StringComparison.OrdinalIgnoreCase)) break;
            directory = directory.Parent;
        }
        if (directory == null) return "Destination must stay inside this project's Assets directory.";
        string absolute = AbsoluteAssetPath(path);
        if (File.Exists(absolute) || Directory.Exists(absolute) || File.Exists(absolute + ".meta") || Directory.Exists(absolute + ".meta")
            || !string.IsNullOrEmpty(AssetDatabase.AssetPathToGUID(path)))
            return "Destination or its metadata already exists. Choose another exact path; nothing will be overwritten.";
        return null;
    }

    private static string SaveAsBlocker(Draft draft, string destination)
    {
        string parameterBlocker = ParameterAssetBlocker(draft);
        if (parameterBlocker != null) return parameterBlocker;
        if (draft.Session.Owner is not ScriptableObject)
            return "Save As requires a standalone ScriptableObject asset, not a scene/component Owner.";
        bool fieldDocument = draft.DocumentField != null;
        if (fieldDocument ? SingleOwnerDocument(draft.Session.Owner.GetType()) != draft.DocumentField
            : draft.Session.Document is not HGAssetDocument)
            return "Save As requires a standalone graph asset or an Owner with exactly one writable document field.";
        if (draft.OwnsOwner) return NewAssetBlocker(draft) ?? DestinationBlocker(destination);
        string source = AssetDatabase.GetAssetPath(draft.Session.Owner);
        if (!AssetDatabase.IsMainAsset(draft.Session.Owner)
            || AssetDatabase.LoadAllAssetsAtPath(source).Count(item => item != null) != 1
            || !fieldDocument && HGDocumentInspection.DocumentFields(draft.Session.Owner.GetType()).Any())
            return "Save As requires a single main asset without subassets or additional document fields.";
        return CommitBlocker(draft, saveAs: true) ?? DestinationBlocker(destination);
    }

    private static object PreviewSaveAs(Draft draft, string destination)
    {
        draft.SaveAsToken = null;
        string blocker = SaveAsBlocker(draft, destination);
        if (blocker != null) return Error("save_as_conflict", blocker);
        string hash = Fingerprint(draft.Session.Document);
        string snapshotId = null;
        var result = HaruGraphInspect.StoreSnapshot(HGDocumentInspection.Capture(draft.Session.Document, "agent_save_as_preview"),
            draft.Session.Owner, draft.Session.DocumentId, workingCopyDirty: draft.Session.IsDirty, onStored: id => snapshotId = id);
        if (snapshotId == null) return result;
        draft.SaveAsPath = destination;
        draft.SaveAsHash = hash;
        draft.SaveAsGeneration = draft.Session.Generation;
        draft.SaveAsFolderGuid = AssetDatabase.AssetPathToGUID(Path.GetDirectoryName(destination).Replace('\\', '/'));
        draft.SaveAsToken = Guid.NewGuid().ToString("N");
        return new SuccessResponse("Preview only; no file created. Save As uses Unity cloning for the source asset's other serialized fields and copies this draft's complete graph. Asset references inside the graph remain unchanged, including references to the source itself. Standalone graph Asset scopes are retargeted to the new GUID; Owner document scopes stay document-local. Validation state is invalidated in the saved copy. Source and its users are not written. Review the snapshot, then use save_as with this exact path, generation and token.", new
        {
            generation = draft.Session.Generation, preview_token = draft.SaveAsToken, snapshot_id = snapshotId,
            source_asset_path = AssetDatabase.GetAssetPath(draft.Session.Owner), destination_path = destination,
            source_asset_revision = draft.AssetRevision, graph_hash = hash, can_save_as = true,
            creates_asset = true, changes_source = false, replaces_references = false,
        });
    }

    private static object SaveAs(Draft draft, string destination, string token)
    {
        if (token == null || token != draft.SaveAsToken || destination != draft.SaveAsPath
            || draft.SaveAsGeneration != draft.Session.Generation || draft.SaveAsHash != Fingerprint(draft.Session.Document))
            return Error("save_as_preview_required", "Use preview_save_as for this exact destination and current generation. Ordinary commit tokens are not accepted.");
        string blocker = SaveAsBlocker(draft, destination);
        if (blocker != null) return Error("save_as_conflict", blocker);
        string folder = Path.GetDirectoryName(destination).Replace('\\', '/');
        if (AssetDatabase.AssetPathToGUID(folder) != draft.SaveAsFolderGuid)
            return Error("save_as_conflict", "Destination folder identity changed. Preview again.");
        draft.SaveAsToken = null;
        // Invalidate source-commit previews too: Save As must not accidentally lead to a second source write.
        draft.PreviewToken = null;
        ScriptableObject copy = null;
        string staging = folder + "/__HaruGraphDraft_" + Guid.NewGuid().ToString("N") + ".asset";
        string createdGuid = null;
        bool creationAttempted = false;
        bool moveAttempted = false;
        try
        {
            var document = draft.Session.Document.DeepCopy() as IGraphDocument;
            if (document == null || ReferenceEquals(document, draft.Session.Document)
                || !draft.Session.Document.GetType().IsInstanceOfType(document))
                throw new InvalidOperationException("Save As requires an isolated document clone of the original type.");
            document.InvalidateValidation();
            copy = UnityEngine.Object.Instantiate((ScriptableObject)draft.Session.Owner);
            copy.name = Path.GetFileNameWithoutExtension(destination);
            copy.hideFlags = HideFlags.None;
            WriteSavedDocument(draft, copy, document);
            if (Fingerprint(ReadSavedDocument(draft, copy, document)) != Fingerprint(document))
                throw new InvalidOperationException("New asset did not retain the complete draft graph.");
            using (var serialized = new SerializedObject(copy))
            {
                var property = serialized.GetIterator();
                while (property.Next(true))
                    if (property.propertyType == SerializedPropertyType.ObjectReference && property.objectReferenceValue != null
                        && property.objectReferenceValue != copy && !EditorUtility.IsPersistent(property.objectReferenceValue))
                        throw new InvalidOperationException("New asset references an unsaved/scene object at " + property.propertyPath + ". Persist or remove that reference before saving.");
            }
            // Recheck after user-defined cloning/serialization code, before any disk creation.
            blocker = SaveAsBlocker(draft, destination) ?? DestinationBlocker(staging);
            if (blocker != null) throw new InvalidOperationException(blocker);
            if (AssetDatabase.AssetPathToGUID(folder) != draft.SaveAsFolderGuid)
                throw new InvalidOperationException("Destination folder identity changed while preparing the asset.");
            creationAttempted = true;
            AssetDatabase.CreateAsset(copy, staging);
            createdGuid = AssetDatabase.AssetPathToGUID(staging);
            if (string.IsNullOrEmpty(createdGuid) || AssetDatabase.LoadMainAssetAtPath(staging) != copy)
                throw new InvalidOperationException("Could not confirm ownership of the staged asset.");
            if (document is HGAssetDocument assetDocument)
            {
                string scope = "ast:" + createdGuid;
                foreach (var group in assetDocument.State.ViewState.NodeGroups)
                {
                    if (group == null) continue;
                    if (group.Scope == assetDocument.Scope) group.SetScope(scope);
                    else if (group.Scope != null && group.Scope.StartsWith(assetDocument.Scope + "/", StringComparison.Ordinal))
                        group.SetScope(scope + group.Scope.Substring(assetDocument.Scope.Length));
                }
                assetDocument.Scope = scope;
            }
            WriteSavedDocument(draft, copy, document);
            string expected = Fingerprint(document);
            if (Fingerprint(ReadSavedDocument(draft, copy, document)) != expected)
                throw new InvalidOperationException("New asset did not retain the saved graph state.");
            string persistedExpected = Fingerprint(document, persistedStrings: true);
            EditorUtility.SetDirty(copy);
            AssetDatabase.SaveAssetIfDirty(copy);
            if (EditorUtility.IsDirty(copy) || !File.Exists(AbsoluteAssetPath(staging))) throw new InvalidOperationException("Staged asset save could not be confirmed.");
            blocker = SaveAsBlocker(draft, destination);
            if (blocker != null) throw new InvalidOperationException(blocker);
            if (AssetDatabase.AssetPathToGUID(folder) != draft.SaveAsFolderGuid)
                throw new InvalidOperationException("Destination folder identity changed while saving the asset.");
            string moveError = AssetDatabase.ValidateMoveAsset(staging, destination);
            if (!string.IsNullOrEmpty(moveError)) throw new InvalidOperationException(moveError);
            moveAttempted = true;
            moveError = AssetDatabase.MoveAsset(staging, destination);
            if (!string.IsNullOrEmpty(moveError)) throw new InvalidOperationException(moveError);
            var saved = AssetDatabase.LoadMainAssetAtPath(destination) as ScriptableObject;
            if (saved == null || AssetDatabase.AssetPathToGUID(destination) != createdGuid || !File.Exists(AbsoluteAssetPath(destination))
                || EditorUtility.IsDirty(saved)
                || Fingerprint(ReadSavedDocument(draft, saved, document), persistedStrings: true) != persistedExpected)
                throw new InvalidOperationException("Destination verification failed after moving the staged asset.");
            bool sourceChanged = draft.OwnsOwner ? NewAssetBlocker(draft) != null
                : AssetRevision(draft.Session.Owner) != draft.AssetRevision || DiskRevision(draft.Session.Owner) != draft.DiskRevision || AssetIsDirty(draft.Session.Owner);
            if (sourceChanged) throw new InvalidOperationException("Source changed during Save As; inspect the source and destination.");
            return new SuccessResponse("Created a new independent asset. Original source, references and draft history are unchanged. The draft still targets the original; open the returned document_id to edit the new asset. No gameplay test was run.", new
            {
                result = "Created", asset_path = destination, asset_guid = createdGuid,
                document_id = HaruGraphInspect.OwnerKey(saved) + "/" + (draft.DocumentField == null ? "$asset" : HGDocumentInspection.FieldKey(draft.DocumentField)), generation = draft.Session.Generation,
                source_asset_path = AssetDatabase.GetAssetPath(draft.Session.Owner), source_unchanged = true,
            });
        }
        catch (Exception exception)
        {
            // Once persistence/move was attempted, do not guess which file is safe to delete or retry.
            // Retain recovery evidence even when a Unity callback throws after the write succeeded.
            if (creationAttempted) draft.RequiresReopen = true;
            return new ErrorResponse("harugraph.draft.save_as_failed", new
            {
                message = exception.Message, requires_reopen = draft.RequiresReopen, creation_attempted = creationAttempted,
                move_attempted = moveAttempted, staging_path = creationAttempted ? staging : null,
                destination_path = destination, asset_guid = createdGuid,
                recovery = creationAttempted ? "Inspect staging and destination before any retry. Files were retained; no automatic deletion was attempted." : "No asset creation was attempted. Fix the reported error and preview again.",
            });
        }
        finally
        {
            if (copy != null && !EditorUtility.IsPersistent(copy)) UnityEngine.Object.DestroyImmediate(copy);
        }
    }
}
}
