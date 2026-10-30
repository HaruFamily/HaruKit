namespace HaruFamily.Tools.GraphMcp.Editor
{
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using HaruFamily.DependencyCore.GraphKit;
using HaruFamily.DependencyCore.GraphKit.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

/// <summary>Explicit trusted Editor integration. Providers must be observational during discovery.</summary>
public sealed class HaruGraphDocumentEntry
{
    public string Key { get; }
    public HGDocumentBinding<IGraphDocument> Binding { get; }
    public HGEditorExtensionContext Context { get; }
    public HaruGraphDocumentEntry(string key, HGDocumentBinding<IGraphDocument> binding, HGEditorExtensionContext context = null)
    { Key = key; Binding = binding ?? throw new ArgumentNullException(nameof(binding)); Context = context ?? HGEditorExtensionContext.Default; }
}

/// <summary>Consumer owns isolation, dependency copies and cleanup. Cancellation is cooperative, not a sandbox.</summary>
public interface IHaruGraphExecutor
{
    string Id { get; }
    string Description { get; }
    bool Supports(IGraphDocument document);
    JObject Describe(IGraphDocument document);
    JObject Run(HaruGraphDraftReadContext snapshot, JObject input, CancellationToken cancellation);
}

public static class HaruGraphExtensions
{
    internal static readonly Dictionary<string, Func<UnityEngine.Object, IEnumerable<HaruGraphDocumentEntry>>> Documents = new(StringComparer.Ordinal);
    internal static readonly Dictionary<string, IHaruGraphExecutor> Executors = new(StringComparer.Ordinal);
    private sealed class Lease : IDisposable
    {
        private Action release;
        internal Lease(Action release) { this.release = release; }
        public void Dispose() { var action = release; release = null; action?.Invoke(); }
    }
    private static void RequireId(string id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128 || id.Any(c => !char.IsLetterOrDigit(c) && c != '.' && c != '_' && c != '-'))
            throw new ArgumentException("Use a stable identifier of 1..128 letters, digits, '.', '_' or '-'.");
    }
    public static IDisposable RegisterDocuments(string id, Func<UnityEngine.Object, IEnumerable<HaruGraphDocumentEntry>> provider)
    {
        RequireId(id); if (provider == null) throw new ArgumentNullException(nameof(provider));
        Documents.Add(id, provider);
        return new Lease(() => Documents.Remove(id));
    }
    public static IDisposable RegisterExecutor(IHaruGraphExecutor executor)
    {
        if (executor == null) throw new ArgumentNullException(nameof(executor));
        RequireId(executor.Id); string id = executor.Id;
        Executors.Add(id, executor);
        return new Lease(() => Executors.Remove(id));
    }
    internal static IEnumerable<(string id, HaruGraphDocumentEntry entry)> Bindings(UnityEngine.Object owner)
    {
        foreach (var provider in Documents.OrderBy(pair => pair.Key, StringComparer.Ordinal).ToArray())
        {
            var entries = (provider.Value(owner) ?? Array.Empty<HaruGraphDocumentEntry>()).Take(65).ToArray();
            if (entries.Length > 64) throw new InvalidOperationException("Document provider exceeds 64 bindings per Owner.");
            var keys = new HashSet<string>();
            foreach (var entry in entries)
            {
                if (entry == null) throw new InvalidOperationException("Null document binding.");
                RequireId(entry.Key);
                if (!keys.Add(entry.Key)) throw new InvalidOperationException("Duplicate document binding key.");
                yield return (HaruGraphInspect.OwnerKey(owner) + "/$binding/" + provider.Key + "/" + entry.Key, entry);
            }
        }
    }
}
}
