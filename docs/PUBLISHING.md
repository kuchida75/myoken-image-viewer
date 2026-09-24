# Source Publication Checklist

The public source repository is [kuchida75/myoken-image-viewer](https://github.com/kuchida75/myoken-image-viewer). The current source version is **0.2.170**; `version.json` is authoritative for later builds. The first Myoken build was 0.2.167, and the earlier Zen Image Viewer source baseline is tagged `v0.2.166`.

## Before Each Source Push

1. Review the diff and run relevant Windows regression checks. Use a separate worktree for a rebuild if the current version must stay unchanged: `tools/build.ps1` advances the build number on every run.
2. Confirm the staged tree contains only reviewed source, tests, assets, docs and notices. The root `.gitignore` is an allowlist; never stage `sources`, `artifacts`, runtime DLLs, AI weights, profiles, sessions, thumbnail/search caches, photos, backups or credentials.
3. Keep the complete root [Apache-2.0 license](../LICENSE), [NOTICE](../NOTICE), and [third-party notices](../THIRD_PARTY_NOTICES.md). The project's license does not relicense downloaded codecs, runtimes or model weights.
4. Check that the app's built-in first-run defaults have no personal settings. A developer's local profile is separate from the source tree; do not reset or publish it merely to make a source commit.
5. Fetch/check GitHub's `main` before pushing. Never force-push over unexpected remote history. Verify the remote commit after the push.

See [building from source](BUILDING.md) and [default state and recovery](DEFAULT_STATE.md).

## Binary Releases

Do not upload the local full ZIP or an executable/model pack until the [third-party distribution review](../THIRD_PARTY_NOTICES.md) is complete. A future binary release needs exact dependency/model versions, checksums, notices, a source revision and test notes.

## Historical Repository Rename

The former `kuchida75/zen-image-viewer` repository was renamed in place. Its public history, issues and Windows baseline tag were retained. Existing clones should use `https://github.com/kuchida75/myoken-image-viewer.git` as `origin`.

Ubuntu/GNOME work can use a separate branch and project in this same repository. The current WPF application remains Windows-only; a shared repository does not make it cross-platform.
