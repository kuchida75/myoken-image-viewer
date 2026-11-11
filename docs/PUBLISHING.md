# Source Publication Checklist

The primary Windows repository is [Myoken on Blackbox](https://192.168.8.100:3000/kuchida/Myoken-Image-viewer-Windows), remote `blackbox`, branch `main`. The current source version is **0.2.249**; `version.json` is authoritative for later builds. Use Blackbox for ordinary pushes and releases. GitHub `origin` is historical and is updated only on explicit request. The first Myoken build was 0.2.167, and the earlier Zen Image Viewer source baseline is tagged `v0.2.166`.

## Before Each Source Push

1. Review the diff and run relevant Windows regression checks. Use a separate worktree for a rebuild if the current version must stay unchanged: `tools/build.ps1` advances the build number on every run.
2. Confirm the staged tree contains only reviewed source, tests, assets, docs and notices. The root `.gitignore` is an allowlist; never stage `sources`, `artifacts`, runtime DLLs, AI weights, profiles, sessions, thumbnail/search caches, private photos, saved face canvases, backups or credentials. The only photographic test fixtures are the pinned, licensed public examples documented in `tools/Fixtures`.
3. Keep the complete root [Apache-2.0 license](../LICENSE), [NOTICE](../NOTICE), and [third-party notices](../THIRD_PARTY_NOTICES.md). The project's license does not relicense downloaded codecs, runtimes or model weights.
4. Check that the app's built-in first-run defaults have no personal settings. A developer's local profile is separate from the source tree; do not reset or publish it merely to make a source commit.
5. Fetch/check Blackbox's `main` before pushing. Never force-push over unexpected remote history. Verify the remote commit after the push. Push to GitHub only when explicitly requested.

See [building from source](BUILDING.md) and [default state and recovery](DEFAULT_STATE.md).

Run `pwsh -NoProfile -File tools/verify-source-publication.ps1` after staging.
It audits the complete index, checks staged/working file identity, source-path
allowlists, fixture hashes, credentials, personal Windows home paths and matching
version metadata. It rejects caches, profile/backup data, linked files and runtime
binaries. After committing, repeat with `-Revision HEAD`; verify the pushed remote
commit and tree match that audited commit. This check does not read or reset the
normal Windows profile, and is a safeguard rather than a guarantee of secret detection.

## Binary Releases

Public executable/model distribution remains pending the [third-party distribution review](../THIRD_PARTY_NOTICES.md). Explicitly requested private Blackbox previews must remain private and marked as prereleases; that does not certify third-party redistribution. Include exact dependency/model versions, checksums, notices, source revision and test notes. Upload binaries as release attachments, never Git source history. Audit ZIP contents for profiles, thumbnails, other caches, saved canvases, backups, credentials and private images, then download the attachments and verify their hashes before publishing the draft release.

## Historical Repository Rename

The former `kuchida75/zen-image-viewer` repository was renamed in place. Its public history, issues and Windows baseline tag were retained. Existing clones should use `https://github.com/kuchida75/myoken-image-viewer.git` as `origin`.

## Ubuntu Migration

The Avalonia/C# port is maintained separately at
[kuchida75/myoken-ubuntu-gnome](https://github.com/kuchida75/myoken-ubuntu-gnome).
The Windows WPF project remains Windows-only.

1. Publish and verify the Windows source first. Record its exact commit and the last Windows commit already ported to Ubuntu.
2. Compare those revisions, using the changelog, feature reference and regression tests as a migration checklist. Port behavior and reusable logic into Avalonia; do not replace compatible Ubuntu implementations or merge the entire WPF project into it.
3. Record the Windows reference commit and ported/deferred items in the Ubuntu repository. Check Linux behavior and GUI parity there, then commit the Ubuntu changes separately.

Blackbox is the source of truth for current Windows work. The GitHub links above describe the historical public repositories; do not mirror there automatically. A small handoff document with sanitized menu/view screenshots can supplement the source when visual parity matters. A separate source ZIP is only an offline/access fallback; label it with the same commit and audit it for private data. Do not use the Windows runtime ZIP as the Ubuntu source handoff.
