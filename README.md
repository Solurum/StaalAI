# StaalAI

This repository contains the StaalAI tooling and related GitHub workflows.

## StaalAI-Execute workflow behavior

When a pull request receives a comment containing the phrase `StaalAI: Execute` and the PR is labeled `StaalAI` by a user with write (or higher) permission, the `.github/workflows/StaalAI-Execute.yml` workflow runs.

The workflow:
- Collects all open (unresolved) PR review threads and formats them (with file paths, authors, URLs, and comment bodies).
- Backs up `lastprompt.txt` to `lastprompt_bak.txt`.
- Temporarily rewrites `lastprompt.txt` by:
  - Adding a note at the top: “Prompt was already executed and there are open review remarks to tackle.”
  - Appending the original prompt content.
  - Adding an “Open Review Remarks” section containing the unresolved remarks.
- Executes `StaalAI generate` with the modified prompt.
- Restores the original `lastprompt.txt` from the backup before committing any changes.
- Commits and pushes any generated changes back to the PR branch, then posts a status comment on the PR.

If there are no open review remarks, the workflow runs StaalAI as before without modifying `lastprompt.txt`.

Requirements:
- A repository secret `PAT` with permissions to read PR metadata and push to the branch.
- A secret `OPENAPITOKEN` and an optional variable `OPENAPIMODEL` for StaalAI execution.

For details, see `.github/workflows/StaalAI-Execute.yml`.
