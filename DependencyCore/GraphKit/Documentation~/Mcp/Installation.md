# GraphKit with optional AI access

Install **GraphKit 1.2.0** once. Its UPM package includes the graph editor, optional MCP integration, portable operation skill and setup window. There is no separate HaruGraph MCP package to install. GraphKit has no mandatory MCP or Newtonsoft JSON dependencies.

## Recommended Unity workflow

1. Install GraphKit from a released UPM tarball or published Git revision. Git URL shape: `https://github.com/HaruFamily/HaruKit.git?path=/DependencyCore/GraphKit#FULL_PUBLISHED_COMMIT`. A local uncommitted candidate is available only as its supplied tarball; do not infer that remote main already contains it.
2. Open **Window → HaruGraph → AI and MCP Setup**. This window works even without Unity MCP. The ordinary graph editor is usable immediately.
3. If you want AI control, install MCP for Unity using the explicit **安裝 Unity MCP** button, or keep your existing compatible installation. The button pins bridge commit 81542a7e86292b5c4c21b1782c5af23b567f45ce; Unity resolves the bridge's dependencies. It does not replace an existing bridge or change client settings. Wait for installation and compilation.
4. Use **安裝操作 skill** to copy the bundled harugraph-mcp skill into the chosen project .agents/skills or .claude/skills directory. An identical file is accepted; a different/customized skill is preserved and reported. Reload skill discovery in your AI client. UPM itself does not register skills.
5. Configure the Unity MCP connection through **AgentDock's existing unityMCP entry**, or MCP for Unity. With AgentDock's stdio definition use the matching Unity bridge transport. Do not create a second HaruGraph MCP server or let two setup tools overwrite each other's client settings. Python/uv runtime and client connection remain the bridge/AgentDock responsibility.
6. Ask the agent to confirm the Unity project path and discover graph documents without modifying assets. No graph window is required. Empty projects may have no graph documents; consumer frameworks/content and execution providers are separate from the generic editor.

Optional integration is enabled only on Unity 6000.3+ with MCP for Unity [10.3.0,11.0.0) and Newtonsoft JSON [3.0.2,4.0.0). The verified Editor is 6000.3.12f1. Unsupported/absent prerequisites leave core GraphKit usable; the setup window reports the bridge and integration status. Core package metadata retains its prior Unity floor, not a new compatibility test for older Editors.

## Release bundle or command line

A bundle contains ONE GraphKit tgz, setup.py, release.json, skill/ and INSTALL.md. Python 3.9+ uses only the standard library. Keep Unity closed for manifest editing. Preview first, then apply:

```text
python setup.py --project "PATH/TO/UNITY_PROJECT"
python setup.py --project "PATH/TO/UNITY_PROJECT" --apply
```

Default installation prepares only GraphKit and the project skill. Add `--with-mcp` to both commands to also prepare the pinned bridge and JSON 3.2.2; Git/network access is needed for those downloads. `--skill-root .claude/skills` selects that discovery root. Existing unrelated settings are preserved, differing dependency sources/skills are rejected, and identical installation is repeatable. The tarball is copied to Packages/HaruGraphDistribution~, so the source bundle can be relocated afterwards.

The helper keeps an exact manifest backup (manifest.harugraph-<hash>.backup~), prepares new files first and atomically replaces the manifest last. Failed preparation may leave unreferenced new files and backup evidence. Review errors before retrying; compare a backup with any subsequent edits before restoring. Installation is not complete until Unity resolves packages and compiles.

For Git installation, run Distribution~/setup.py from the complete GraphKit package with `--harukit-revision FULL_40_CHARACTER_PUBLISHED_COMMIT`. It adds ONE GraphKit Git entry. Use the same published revision for the helper and package. Git authentication may be needed; never embed credentials in the URL. Unity supports Git dependencies in a project's manifest, not between package manifests.

For skill-only export after UPM installation, run `python "PACKAGE_PATH/Distribution~/setup.py" --project "PROJECT_PATH" --skill-only --apply`, or use the setup window. Both leave client configuration alone; the Python helper also leaves package dependencies alone.

## Upgrade from separate GraphMcp

Close Unity. In Packages/manifest.json, remove com.harufamily.tools.graphmcp from dependencies and testables, and update GraphKit to the integrated version in the SAME edit. Keep your existing MCP for Unity source. Leave only com.harufamily.dependencycore.graphkit in testables for these package tests. Unity regenerates the lock as needed. Do not retain embedded/manual copies of the old MCP assemblies alongside GraphKit: both use the same assembly identities and would conflict. The helper deliberately stops when the retired package entry exists rather than silently deleting it.

MCP assembly names, namespaces, four tool names, and moved script/assembly GUIDs are preserved. Existing consumer executor/binding references therefore do not need renaming. Code moves do not migrate or rewrite graph assets. Reload the Editor/client tool inventory and reopen drafts after the upgrade; in-memory draft/preview tokens are not migration artifacts.

## First verification

Confirm haru_graph_inspect, haru_graph_draft, haru_graph_execute and haru_graph_transaction are exposed (some clients call them through batch_execute). Use search_documents, then test editing/saving on a temporary graph. A successful connection is not proof that a consumer execution provider is registered. Follow AgentWorkflow.md or the skill for actual graph operations.

Sources: [Unity assembly conditions](https://docs.unity3d.com/6000.0/Documentation/Manual/assembly-definition-file-format.html), [Unity Git dependencies](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/upm-git), [pinned MCP bridge](https://github.com/CoplayDev/unity-mcp/tree/81542a7e86292b5c4c21b1782c5af23b567f45ce).
