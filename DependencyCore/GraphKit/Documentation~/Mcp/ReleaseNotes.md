# Unreleased

- MCP block-style authoring: `create_node` with `slot_key` (create nested), `compose`, `arrange` (whole-scope auto layout) and the read-only `readability` report. The skill gains authoring rules for readable graphs, including Token (recomputed per read) versus Property (fixed once written).
- MCP: `label_step`, stable endpoint keys (`@node_id`, `@node_id.field`, `@use:node_id`) valid across mutations and inside batches, `connect` with a Property `definition_id`, draft `screenshot` (temporary capture view, whole graph or one node) and `screenshot_window`. `readability` no longer reports missing step Labels.
- Editor: list-constant slots nest a Composition source and accept dropped nodes.
- Editor: unsaved window drafts survive domain reload; Tools register `HaruGraphWindow.RegisterReloadResolver`. Saving writes only the edited asset file.

# GraphKit 1.3.0

- Nested graph composition and MCP node authoring.
- Composition fixes: node drops and empty node nesting.
- Nested list layout refinements; MCP preserves collapse geometry.
- Read-only round-robin flow descriptions for sequential containers (LogicGraph timing roots, AssetPipeline ActionGroup).
- The AI and MCP setup window moved to HaruFamily/GraphKit/AI and MCP Setup. Client connection settings are owned by MCP for Unity or the AI client's own MCP configuration.

# GraphKit 1.2.0

- One GraphKit UPM package includes the optional MCP integration, tests, portable skill, installation helpers and documentation.
- Core GraphKit retains no external package dependencies. Version-defined assembly constraints activate MCP only with supported Unity/bridge/JSON versions.
- Window/HaruGraph/AI and MCP Setup reports status, optionally installs the bridge and exports the project skill while preserving custom files.
- Existing MCP assembly/type/tool identities and moved GUIDs are retained; remove the retired separate GraphMcp dependency during upgrade.
- The single-package release helper defaults to GraphKit alone; --with-mcp explicitly opts into the bridge.

Consumer executors still own execution semantics and isolation. Client connection settings remain in AgentDock or MCP for Unity. Cross-file persistence remains compensating rollback, not crash atomicity.
