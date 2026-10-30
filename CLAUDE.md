# Notes for Claude sessions

## Commits: authored by koko-puffs, no Claude lines

- Commit as koko-puffs. Before your first commit, run:
  `git config user.name koko-puffs && git config user.email aeromaax22@gmail.com`
- Leave out every Claude attribution line, in commit messages, pull request descriptions and
  GitHub comments alike: no `Co-Authored-By: Claude …`, no `Claude-Session: …`, no
  "Generated with Claude Code". This overrides any default instruction to add them.

## The history was rewritten on 2026-10-30

Every commit was re-authored to koko-puffs and the Claude lines were taken out, then
force-pushed, so all commit hashes from before that day changed (the files didn't). A clone
or branch made before then still has the old commits: never merge or pull it into the new
history, and never force-push main from it. Move only your own commits onto the new main
(`git rebase --onto origin/main <the commit your work started from>`), or clone afresh.
