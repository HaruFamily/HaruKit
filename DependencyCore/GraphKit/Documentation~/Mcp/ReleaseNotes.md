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
