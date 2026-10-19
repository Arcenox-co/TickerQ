# Synchronizing .NET version branches

The version-sync workflow defaults to `main-commercial` as its source. Manual runs
can select `source_branch`; release-triggered runs use `SYNC_SOURCE_BRANCH` (default
`main-commercial`). Releases targeted at another named branch are skipped. The
release tag must belong to the selected source, and only that tag's commit is merged,
so newer unreleased commits are not included. Manual runs synchronize the selected
source branch's current commit.

`target_branches` is a comma-separated selection of `net8` and/or `net9`; otherwise
`TARGET_BRANCHES` supplies the JSON default. The workflow creates review PRs, not
releases. Use `dry_run` to prepare and validate the merge without pushing it.

`.sync-overrides` changes `VersionPrefix`, `TargetFramework` and the .NET dependency
range. For example, `10.5.0-rc.1` becomes `8.5.0-rc.1` or `9.5.0-rc.1`. Target-specific
abstraction ranges are preserved. Missing, duplicated or invalid required properties
stop the workflow before overrides are written; they are never silently skipped.

Files in `.sync-exclude` retain their target implementation. Review those files for
manual backports whenever the source changes them, especially EF registration and
query APIs. Build, test and inspect the actual target packages before releasing them.
Unresolved code conflicts outside explicit overrides/exclusions stop synchronization
for manual resolution. This workflow does not certify .NET 8/9 compatibility by itself.

Local override checks:

```sh
python3 -m unittest scripts.tests.test_sync_version_overrides -v
```
