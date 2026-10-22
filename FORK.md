# Dezoxy/lingarr fork

A temporary fork of [lingarr-translate/lingarr](https://github.com/lingarr-translate/lingarr) that runs
Anthropic batch translation on current Claude models until upstream ships a fix.

## What it changes

One commit on top of the upstream release tag (`fix/anthropic-batch-structured`):

- The Anthropic batch tool is `strict` and called with `tool_choice: auto`, because current models
  reject forced tool use and non-strict tools can return the array as a string.
- The default Anthropic `max_tokens` is 16000 instead of 1024.

The fix is based on a release tag, not `main`, so the database schema matches that release exactly
and switching back to the upstream image is safe.

## Branches

| Branch | Contents |
| --- | --- |
| `homelab` (default) | Upstream release tag + the fix + fork CI. Images are built from here. |
| `fix/anthropic-batch-structured` | Only the fix, used for the upstream pull request. |
| `main` | Untouched mirror of upstream. |

## Releasing an image

CI (`.github/workflows/fork-ci.yml`) tests every push. A tag `v<upstream version>-anthropic.<n>` on `homelab`
also publishes `ghcr.io/dezoxy/lingarr:<tag>` with an SBOM and a provenance attestation; the run summary
shows the digest to pin.

```bash
git tag v1.4.0-anthropic.1 homelab
git push origin v1.4.0-anthropic.1
gh attestation verify oci://ghcr.io/dezoxy/lingarr:v1.4.0-anthropic.1 --repo Dezoxy/lingarr
```

## Retiring the fork

When an upstream release contains the fix, switch the deployment back to `lingarr/lingarr:<version>`
and archive this repository.

Lingarr is licensed under AGPL-3.0; this fork's source is public to satisfy it.
