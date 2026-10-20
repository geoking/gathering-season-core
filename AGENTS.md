# Gathering Season Core working agreement

## Scope

This repository powers the Gathering Season gameplay backend and playable CLI.
It contains the engine-independent rules, the current catalogue, tests and evaluation
tools. It does not include the Gathering Season UI game.

Keep contributions focused on developing, using and testing this repository.
Documentation covers running the CLI, currently implemented gameplay rules and
Core API/save contracts. Apply the same scope to commit messages, pull requests,
issues and CI artifacts.
Run tools/check_public_boundary.py against staged files before every commit.

## Architecture and rules

Core is engine-independent. Clients consume detached observations and issued
legal actions rather than calculating their own rewards. Preserve information
boundaries, deterministic random continuation and supported current saves.
Keep the rule guides, executable catalogue and numeric test fixtures in sync.
Do not infer rules from names or artwork; ask about unresolved rule decisions.
Namespaces and assemblies use Gathering Season consistently. Core v2 executes
only rules revision 8; keep format 1, the stable profile identity and numeric
enum values unchanged. Do not reintroduce historical rule execution or revision
selection. Unsupported saves must be rejected before state construction and
failed Continue must preserve source files.

## Working method

Use GPT-6.1 Sol/high for Core changes and complex tests; GPT-6 Luna/medium for
narrow support work. Explicitly select both model and reasoning effort. At most
two workers may run concurrently, with disjoint file ownership and no further
delegation. Workers do not stage or commit. The lead owns review and Git.

Preserve unrelated changes. Use codex/ branches and coherent checkpoints. Run
focused checks during implementation and full checks at completed steps. Commit
Core behavior and its regression tests separately, then push each checkpoint.
After the relevant checks pass, merge completed, authorized work to main without
another review or permission request. Keep pull requests as change records.
Do not force-push without a request. New rules need approval.

## Necessary testing

Add or update automated tests only for a meaningful behaviour, supported
contract, failure path or credible regression risk not already covered. Feature
size alone is not a reason to add tests. Inspect existing coverage first and
prefer a focused update to an overlapping fixture. Small reversible copy or
documentation changes may need only the relevant build, CLI check or review.

Test observable outcomes at the lowest useful layer. Avoid trivial
constructors/getters/constants, source-text and private implementation-shape
checks, exact prose snapshots and exhaustive combinations unless they protect a
specific material contract. Retain parameter cases for distinct branches,
boundaries and supported current saves; do not move redundant rows into loops
merely to lower the reported count. Preserve meaningful rule, information-boundary,
arithmetic, save-recovery and deterministic-continuation coverage.

For a new test, identify the uncovered risk. For a removed test, identify its
replacement coverage or the retired contract. Remove superseded assertions and
unused helpers when behaviour changes. Record appropriate validation rather
than requiring a test-count or coverage-percentage target. Use focused checks
while iterating, then the relevant full suite at the completed step; repeat it
only for new changes, failures or unresolved concerns.

## Versioned releases

Publish approved stable Core distributions as GitHub releases with a matching
Git tag and `GatheringSeason.Core.dll` attached. The initial release is `v1`;
use `v1.1` and later minor versions for compatible incremental releases, and
`v2` or later major versions for substantial or incompatible upgrades. Patch
versions may identify compatible corrections when appropriate. Release versions
are separate from rules revisions and save/catalogue compatibility identifiers.

When a distributed application adopts a newer Core revision, publish the
corresponding Core version before that application is released. Reuse an existing
release when its exact DLL is unchanged. Keep published tags and assets fixed;
ship corrections under a new version instead of replacing a published DLL.
Core uses versioned tags/releases, not date-based release branches. Follow
[the release procedure](README.md#releases), retaining the exact source commit,
build command and DLL checksum. No release is authorised merely by these rules.
