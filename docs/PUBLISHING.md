# Source Publication Checklist

The public source repository is [kuchida75/myoken-image-viewer](https://github.com/kuchida75/myoken-image-viewer). The current source version is **0.2.227**; `version.json` is authoritative for later builds. The first Myoken build was 0.2.167, and the earlier Zen Image Viewer source baseline is tagged `v0.2.166`.

## Before Each Source Push

1. Review the diff and run relevant Windows regression checks. Use a separate worktree for a rebuild if the current version must stay unchanged: `tools/build.ps1` advances the build number on every run.
2. Confirm the staged tree contains only reviewed source, tests, assets, docs and notices. The root `.gitignore` is an allowlist; never stage `sources`, `artifacts`, runtime DLLs, AI weights, profiles, sessions, thumbnail/search caches, private photos, saved face canvases, backups or credentials. The only photographic test fixtures are the pinned, licensed public examples documented in `tools/Fixtures`.
3. Keep the complete root [Apache-2.0 license](../LICENSE), [NOTICE](../NOTICE), and [third-party notices](../THIRD_PARTY_NOTICES.md). The project's license does not relicense downloaded codecs, runtimes or model weights.
4. Check that the app's built-in first-run defaults have no personal settings. A developer's local profile is separate from the source tree; do not reset or publish it merely to make a source commit.
5. Fetch/check GitHub's `main` before pushing. Never force-push over unexpected remote history. Verify the remote commit after the push.

See [building from source](BUILDING.md) and [default state and recovery](DEFAULT_STATE.md).

Run `pwsh -NoProfile -File tools/verify-source-publication.ps1` after staging.
It audits the complete index, checks staged/working file identity, source-path
allowlists, fixture hashes, credentials, personal Windows home paths and matching
version metadata. It rejects caches, profile/backup data, linked files and runtime
binaries. After committing, repeat with `-Revision HEAD`; verify the pushed remote
commit and tree match that audited commit. This check does not read or reset the
normal Windows profile, and is a safeguard rather than a guarantee of secret detection.

## Binary Releases

Do not upload the local full ZIP or an executable/model pack until the [third-party distribution review](../THIRD_PARTY_NOTICES.md) is complete. A future binary release needs exact dependency/model versions, checksums, notices, a source revision and test notes.

## Historical Repository Rename

The former `kuchida75/zen-image-viewer` repository was renamed in place. Its public history, issues and Windows baseline tag were retained. Existing clones should use `https://github.com/kuchida75/myoken-image-viewer.git` as `origin`.

## Ubuntu Migration

The Avalonia/C# port is maintained separately at
[kuchida75/myoken-ubuntu-gnome](https://github.com/kuchida75/myoken-ubuntu-gnome).
The Windows WPF project remains Windows-only.

1. Publish and verify the Windows source first. Record its exact commit and the last Windows commit already ported to Ubuntu.
2. Compare those revisions, using the changelog, feature reference and regression tests as a migration checklist. Port behavior and reusable logic into Avalonia; do not replace compatible Ubuntu implementations or merge the entire WPF project into it.
3. Record the Windows reference commit and ported/deferred items in the Ubuntu repository. Check Linux behavior and GUI parity there, then commit the Ubuntu changes separately.

GitHub is the source of truth. A small handoff document with sanitized menu/view screenshots can supplement it when visual parity matters. A separate source ZIP is only an offline/access fallback; label it with the same commit and audit it for private data. Do not use the Windows runtime ZIP as the Ubuntu source handoff.
