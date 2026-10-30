# GraphKit distribution and validation

Build a single GraphKit package with `python Distribution~/build_release.py --output NEW_DIRECTORY_OUTSIDE_HARUKIT`. The builder includes Mcp/, AgentSkill~, Distribution~, Validation~ and documentation, preserving existing .meta identities. It never commits, tags, pushes or creates a remote release. release.json records source revision, dirty state, package version and archive SHA-256. A dirty snapshot is a local candidate.

Before publication:

1. Run Distribution~/test_distribution.py. Verify default GraphKit-only installation and opt-in --with-mcp, conflict handling, skill export and repeatability.
2. Install the resulting GraphKit tarball in a clean project with test framework and GraphKit testables, WITHOUT MCP or Newtonsoft JSON. Verify GraphKit compiles/tests pass and the optional MCP assembly is absent. Do not count optional tests as skipped failures: their assembly must not be compiled.
3. Install the SAME tarball in a second clean project with the supported bridge/JSON, using --with-mcp. Verify both core and optional MCP test assemblies run. Record lock-file sources and results.
4. Upgrade the existing consuming project by removing its old GraphMcp package dependency/testable while updating GraphKit. Run consumer integration tests and verify custom tool registration. Retain moved GUIDs and consumer assembly identities.
5. When remote publication is authorized, commit reviewed content, rebuild from a clean revision and compare tested archive contents; push HaruKit before sharing its full-commit Git URL or updating the consuming repository's submodule pointer. Upload the complete bundle or a zip preserving its directory layout. Do not replace a previously released version/tag with different contents.

GraphKit's package manifest must remain free of forced MCP/JSON dependencies. Version defines and constraints on BOTH optional assemblies gate Unity version, bridge and JSON. The ordinary setup window must not reference either optional assembly. Keep core-only and integrated validation distinct.

Remote Git/registry publication and a new AI-client connection are separate evidence from local UPM installation. There is no separate GraphMcp artifact to publish from this version.
