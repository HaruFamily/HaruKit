# 0.2.0

- Reusable graph discovery, authoring, registered execution and dependency-aware multi-document persistence.
- Explicit semantic package dependencies, with pinned project-level Git installation and local UPM tarball distribution.
- Self-contained portable harugraph-mcp skill and project-local installer with preview, conflict checks, manifest backup and repeatable application.
- Reproducible release builder, package hashes and documented publication procedure.
- Unity support floor now states 6000.3, matching the verified release line. Older 2021.3 metadata did not establish compatibility with that version.

Execution still requires a consumer provider. UPM installation does not configure an AI client/server. Multi-file saving uses compensating rollback, not crash-atomic persistence.
