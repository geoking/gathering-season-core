# Core releases

A release identifies an exact source revision and its built Core DLL. Stable
releases use Git tags and GitHub releases: `v1` initially, `v1.1` for compatible
incremental releases, and `v2` or later for substantial or incompatible upgrades.
Compatible corrections may use patch versions. Release versioning does not
change rules revisions or the supported save/catalogue contracts by itself.

Publish a new Core version when a distributed application adopts a changed
Core revision. Reuse the existing version when its exact DLL is unchanged.
Published tags and assets remain fixed; a correction receives a new version.

1. Finish the approved changes and merge the reviewed branch. Select the exact
   clean source commit and confirm the release tag is unused.
2. Run the checks in the [README](../README.md#verify), including the CLI demo
   used by CI. Build with the Release configuration and
   `-p:ContinuousIntegrationBuild=true`. Core targets .NET Standard 2.1.
3. Verify the DLL assembly version matches the release (`v1` is `1.0.0.0`),
   and record its SHA-256, source commit, SDK and build command. Keep runtime
   version metadata aligned when preparing later releases.
4. Create a draft GitHub release targeting the exact verified commit. Attach
   `GatheringSeason.Core.dll` and `GatheringSeason.Core.dll.sha256`. Release
   notes describe the implemented backend/CLI and supported contracts, with
   the build provenance and relevant validation results.
5. Read back the draft asset and verify its bytes and checksum before
   publication. Publish the approved release, then verify the public tag,
   downloadable DLL and checksum. Do not replace an existing published asset.

The DLL has no game-engine dependency and contains the gameplay backend.
Source archives are provided by GitHub. The repository's MIT licence applies.
