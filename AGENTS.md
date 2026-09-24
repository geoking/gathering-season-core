# Gathering Season Core working agreement

## Scope

This repository contains the standalone gameplay backend, playable CLI, rule
catalogues, tests and evaluation tools. It does not include the Gathering Season
UI game. Keep changes and documentation focused on the code in this repository.

Document how to run the CLI, the currently implemented gameplay rules and the
Core API/save contracts. Describe supported behavior, not future product plans.
UI implementation, screenshots, recordings and artwork are outside this scope.
Commit messages, pull requests, issues and CI artifacts follow the same scope.
Run tools/check_public_boundary.py against staged files before every commit.

## Architecture and rules

Core is engine-independent. Clients consume detached observations and issued
legal actions rather than calculating their own rewards. Preserve information
boundaries, deterministic random continuation and supported save catalogues.
Keep the rule guides, executable catalogues and numeric test fixtures in sync.
Do not infer rules from names or artwork; ask about unresolved rule decisions.
Namespaces and assemblies use Gathering Season consistently.

## Working method

Use GPT-6 Sol/high for Core changes and complex tests; GPT-6 Luna/medium for
narrow support work. Explicitly select both model and reasoning effort. At most
two workers may run concurrently, with disjoint file ownership and no further
delegation. Workers do not stage or commit. The lead owns review and Git.

Preserve unrelated changes. Use codex/ branches and coherent checkpoints. Run
focused checks during implementation and full checks at completed steps. Commit
Core behavior and its regression tests separately, then push each checkpoint.
Do not force-push or merge to main without a request. New rules need approval.
