# Install HaruGraph MCP

HaruGraph MCP 0.2.0 is an optional Editor package. This release targets Unity 6000.3; the verified Editor is 6000.3.12f1. It needs GraphKit 1.1.1, MCP for Unity 10.3.0 and Newtonsoft JSON 3.2.2. Python 3.9+ and Git are needed for the assisted installation; Python uses only its standard library. Git/network access is needed to fetch the pinned MCP bridge; Unity also resolves registry dependencies.

## Recommended: release bundle

Keep Unity closed while changing project dependencies. Extract the release bundle into a directory. It contains two ordinary UPM .tgz packages, setup.py, release.json, this guide and the portable skill. It does not redistribute the third-party bridge or contain game Assets.

From that directory, preview installation, then apply the same plan:

```text
python setup.py --project "PATH/TO/UNITY_PROJECT"
python setup.py --project "PATH/TO/UNITY_PROJECT" --apply
```

The installer verifies archive hashes, copies the two tarballs into Packages/HaruGraphDistribution~, merges the required direct dependencies into Packages/manifest.json and installs the portable skill at .agents/skills/harugraph-mcp/SKILL.md. Existing unrelated manifest settings are preserved. Choose `--skill-root .claude/skills` if that is the consuming agent's skill discovery location. Use the same option for preview and apply. No user-wide files or AI connection settings are changed.

Existing dependencies with different source/version strings, or a differing installed skill/archive, stop the whole preparation before changes. Review the conflict and reconcile it deliberately; there is no force-overwrite switch. Repeating an identical installation is a no-op. A changed manifest has an exact-byte backup named Packages/manifest.harugraph-<hash>.backup~. The manifest is activated last with an atomic replacement. A failed preparation may leave identical unreferenced new files and a backup; inspect the error, then rerun. Do not run simultaneously with Unity or another manifest editor. When undoing, compare the backup with subsequent project changes rather than blindly replacing the manifest.

Open Unity and wait for Package Manager and compilation to finish. The installer prepares files; only successful Unity package resolution/compilation proves installation succeeded. Track manifest.json, packages-lock.json, the two tarballs and the installed project skill in your own version control when ready.

## Git UPM installation

Unity supports Git sources in the PROJECT manifest, not Git dependencies inside a PACKAGE manifest. HaruGraph declares semantic version dependencies; the project must supply GraphKit and MCP bridge sources. Adding only the GraphMcp Git URL to an otherwise empty project is not sufficient.

For a published HaruKit commit, obtain this package's Distribution~/setup.py together with AgentSkill~ (or use a repository checkout), then run:

```text
python "PATH/TO/GraphMcp/Distribution~/setup.py" --project "PATH/TO/UNITY_PROJECT" --harukit-revision FULL_40_CHARACTER_COMMIT
python "PATH/TO/GraphMcp/Distribution~/setup.py" --project "PATH/TO/UNITY_PROJECT" --harukit-revision FULL_40_CHARACTER_COMMIT --apply
```

Use the SAME published commit for the helper/skill and both HaruKit packages. The script pins both packages to that revision and MCP for Unity to the audited commit 81542a7e86292b5c4c21b1782c5af23b567f45ce; it does not follow main. The revision must actually be available remotely. Git installation may require authentication to HaruFamily/HaruKit; do not embed credentials in URLs.

Manual UPM installation is also supported: add all three Git packages explicitly (GraphKit path DependencyCore/GraphKit, GraphMcp path Tools/GraphMcp, bridge path MCPForUnity), using the pinned revisions above. Newtonsoft JSON is resolved by the declared dependency. A local release bundle avoids relying on a published HaruKit Git revision.

## Skill after UPM installation

UPM does not register agent skills automatically. From the resolved package location (Package Manager's package location, commonly under Library/PackageCache), run:

```text
python "PACKAGE_PATH/Distribution~/setup.py" --project "PATH/TO/UNITY_PROJECT" --skill-only --apply
```

This leaves dependencies alone and copies the self-contained bundled skill to the selected project skill root. An existing customized skill is never overwritten. The AgentSkill~ directory is included in both Git UPM and tarball distributions but ignored by Unity's asset importer. You can alternatively copy its harugraph-mcp folder manually to your agent's supported project skill directory. Reload skill discovery according to that client's workflow.

## Connect and first use

1. Open the MCP for Unity window and configure/start its server using the bridge's setup instructions. Connect your AI client's MCP configuration to that server. Check project identity, not just connection status. Server runtime/client configuration is a separate step; the UPM installer does not install Python/uv, start services or edit credentials.
2. Confirm the four tools are discovered: haru_graph_inspect, haru_graph_draft, haru_graph_execute and haru_graph_transaction. If a client routes Unity custom tools through batch_execute, invoke these names in its commands rather than assuming each appears as a top-level client tool.
3. Ask the agent to discover documents with inspect action search_documents; no graph window is needed. An empty fresh project legitimately has no documents. Install your consuming graph framework/content as needed.
4. Follow AgentWorkflow.md or the bundled skill to edit and save. Execution requires a consumer to register an executor; generic installation alone does not supply gameplay or pipeline execution semantics.

Suggested first request: "Confirm the connected Unity project, discover available graph documents and report their identities without changing assets." Then choose a temporary asset for the first edit/save/reopen test.

Sources: [Unity Git dependencies](https://docs.unity.com/en-us/engine/6000.0/manual/packages-list/upm-git), [Unity tarball installation](https://docs.unity.com/en-us/engine/6000.3/manual/packages-list/managing-packages-window/upm-ui-actions/upm-ui-tarball), [MCP for Unity at the pinned revision](https://github.com/CoplayDev/unity-mcp/tree/81542a7e86292b5c4c21b1782c5af23b567f45ce).
