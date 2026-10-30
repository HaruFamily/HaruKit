namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Linq;
using System.Reflection;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using MCPForUnity.Editor.Helpers;
using UnityEditor;
using UnityEngine;

public static partial class HaruGraphDraft
{
    private static FieldInfo SingleOwnerDocument(Type type)
    {
        if (!typeof(ScriptableObject).IsAssignableFrom(type) || typeof(IGraphAsset).IsAssignableFrom(type)) return null;
        var fields = HGDocumentInspection.DocumentFields(type);
        return fields.Count == 1 && !fields[0].IsInitOnly ? fields[0] : null;
    }

    private static Type[] CreatableOwnerTypes() => HGTypeIndex.Concrete(typeof(ScriptableObject))
        .Where(type => SingleOwnerDocument(type) != null)
        .OrderBy(type => type.AssemblyQualifiedName, StringComparer.Ordinal).ToArray();

    private static object OwnerTypes(ToolParams input)
    {
        int offset = input.GetRaw("offset")?.ToObject<int>() ?? 0;
        int pageSize = input.GetRaw("page_size")?.ToObject<int>() ?? 25;
        if (offset < 0 || pageSize < 1 || pageSize > 100) return Error("invalid_page", "offset >= 0 and page_size 1..100 are required.");
        string search = input.Get("search");
        var types = CreatableOwnerTypes().Where(type => string.IsNullOrEmpty(search)
            || type.FullName.IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        return new SuccessResponse("Concrete ScriptableObject owners with exactly one writable serialized IGraphDocument field. No constructors invoked; test assemblies, graph assets and multi-document owners are excluded. new_owner uses owner defaults and creates an empty declared document only if the field is null and concrete. Other owner settings and resource registration are outside this graph tool.", new
        {
            total = types.Length, next_offset = (long)offset + pageSize < types.Length ? (int?)(offset + pageSize) : null,
            entries = types.Skip(offset).Take(pageSize).Select(type => new
            {
                type = type.AssemblyQualifiedName, label = HGReflect.TypeName(type),
                document_field = HGDocumentInspection.FieldKey(SingleOwnerDocument(type)),
                document_type = SingleOwnerDocument(type).FieldType.AssemblyQualifiedName,
            }).ToArray(),
        });
    }

    private static object NewOwner(string typeName)
    {
        if (drafts.Count >= 16) return Error("draft_limit", "Discard a previous draft before creating another (maximum 16).");
        var type = CreatableOwnerTypes().SingleOrDefault(candidate => candidate.AssemblyQualifiedName == typeName);
        if (type == null) return Error("owner_type_unavailable", "Select an exact type from owner_types; arbitrary types and test assemblies are not accepted.");
        ScriptableObject owner = null;
        bool retained = false;
        try
        {
            owner = ScriptableObject.CreateInstance(type);
            owner.name = type.Name;
            owner.hideFlags = HideFlags.HideAndDontSave;
            var field = SingleOwnerDocument(type);
            var live = field.GetValue(owner) as IGraphDocument;
            if (live == null)
            {
                if (field.FieldType.IsAbstract || field.FieldType.IsInterface || field.FieldType.ContainsGenericParameters
                    || field.FieldType.GetConstructor(Type.EmptyTypes) == null)
                    return Error("document_unavailable", "Owner default document is null and its declared type cannot be constructed. No file created.");
                live = HGReflect.CreateInstance(field.FieldType) as IGraphDocument;
                if (live == null) return Error("document_unavailable", "Could not create the declared document. No file created.");
                field.SetValue(owner, live);
            }
            string id = Guid.NewGuid().ToString("N");
            string revision = TransientRevision(owner);
            var baseline = HGDocumentInspection.Capture(live, "agent_baseline");
            string hash = Fingerprint(live);
            var binding = new HGDocumentBinding<IGraphDocument>("new:" + id + "/" + HGDocumentInspection.FieldKey(field),
                target => field.GetValue(target) as IGraphDocument, (target, document) => field.SetValue(target, document),
                create: null, readRevision: TransientRevision);
            if (!HGDocumentSession<IGraphDocument>.TryOpenPreservingOwner(owner, binding, out var session, out var diagnostic))
                return new ErrorResponse("harugraph.draft.open_failed", new { diagnostic });
            if (!ReferenceEquals(field.GetValue(owner), live) || TransientRevision(owner) != revision)
                return Error("source_changed_during_open", "A provider changed the temporary owner while opening; no draft retained.");
            var draft = new Draft { Session = session, OwnsOwner = true, DocumentField = field, Expires = DateTime.UtcNow.AddMinutes(30),
                ReadDocument = () => field.GetValue(owner) as IGraphDocument, BaselineDocument = live, Baseline = baseline,
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

    private static void WriteSavedDocument(Draft draft, ScriptableObject owner, IGraphDocument document)
    {
        if (draft.DocumentField != null) draft.DocumentField.SetValue(owner, document);
        else ((IEditableGraphAsset)owner).ApplyEditorState(((HGAssetDocument)document).State);
    }

    private static IGraphDocument ReadSavedDocument(Draft draft, ScriptableObject owner, IGraphDocument document)
        => draft.DocumentField != null ? draft.DocumentField.GetValue(owner) as IGraphDocument
            : new HGAssetDocument(((IEditableGraphAsset)owner).CaptureEditorState(), ((HGAssetDocument)document).Scope);
}
}
