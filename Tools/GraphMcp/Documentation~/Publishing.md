# Release procedure

The distribution builder produces local release artifacts; it never commits, tags, pushes or publishes remotely. Keep release evidence separate from an external publication claim.

1. Review the package versions/dependency floors, portable skill and Installation.md. The MCP bridge revision in Distribution~/setup.py must correspond to the tested bridge, not a moving branch. GraphKit remains independent of GraphMcp.
2. Run `python Distribution~/test_distribution.py` and the package's clean-project Unity tests. Also test a new project using the release tarballs through UPM, with no embedded copies of GraphKit/GraphMcp. Record source hashes, dependency resolution and actual results.
3. Build outside the repository with `python Distribution~/build_release.py --output NEW_RELEASE_DIRECTORY`. Archives use the UPM package/ root and preserve .meta identities. release.json records versions, archive SHA-256, source revision and whether the source worktree was dirty. A dirty snapshot is a local candidate, not a committed release.
4. For an approved remote publication, commit reviewed changes, rebuild from that clean revision and verify archive contents. Push the HaruKit commit before sharing its Git URLs; update/push any consuming repository's submodule reference afterwards. Use a versioned release/tag such as graphmcp-v0.2.0 only when it points to that exact tested content. Do not replace an existing version/tag with different content.
5. Upload the bundle's archives, setup.py, release.json, skill/ and INSTALL.md together, or zip that directory as a release asset. Preserve the layout so setup.py can find release.json. Git consumers instead use the full published commit for both HaruKit packages.

No registry or GitHub Release is created by these scripts. Registry publication would require a configured registry that actually hosts GraphKit and the bridge's semantic versions; do not invent scopedRegistries to simulate that. Tarball and Git project-manifest installation require no custom registry.

After publication, validate a download from the published location in a new project before declaring remote installation verified. Local tarball tests and offline package-copy tests do not prove remote publication or a new AI-client connection.
