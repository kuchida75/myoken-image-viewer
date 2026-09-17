# Publication Checklist

Target: public repository `kuchida75/zen-image-viewer`.
Baseline: Windows version `0.2.166`.

## Before The First Push

1. The owner selected Apache-2.0 for original project code and documentation.
   Keep the complete root `LICENSE`, `NOTICE`, and all third-party exceptions
   and notices. This does not relicense downloaded runtimes or model weights.
2. Create an empty public GitHub repository, without GitHub-generated README,
   license or ignore files, so the first commit can come from this source tree.
3. Review the staged file list and diff. Confirm no `sources`, `artifacts`,
   runtime DLLs, AI weights, profiles, photos or credentials are staged. The
   root `.gitignore` is an allowlist; add future root files intentionally.
   Verify default startup/reset behavior without embedding a personal profile.
4. Make the initial source commit, then tag it `v0.2.166` as the Windows source
   baseline. Do not rebuild just to publish: builds advance the version.
5. Push `main` and that tag to the exact target repository. Never force-push
   over unexpected remote history. Verify the remote commit and public
   visibility after upload.

Read [BUILDING.md](BUILDING.md) for a fresh source clone. The native runtime and
model files restored locally remain ignored by Git. The prepared repository
does not claim that a completely clean machine build was tested during this
documentation-only preparation.

## Releases

Do not upload the existing full ZIP or an executable/model pack until the
[third-party distribution review](../THIRD_PARTY_NOTICES.md) is completed.
Git history is for source; independently reviewed release assets can be
published later with checksums, notices, exact source revision and test notes.

## Ubuntu Work Later

Use a separate branch and checkout/worktree in the same repository. Keep the
Windows baseline tag and existing WPF implementation intact. Add the Linux UI
as a separate project under `src`, and extract shared code deliberately with
Windows regression tests. The current project is not cross-platform simply
because its source is on GitHub. No Ubuntu project, branch or implementation
is created by this publication preparation.
