# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project state

Empty scaffold — no source code, build system, or tests yet. There is nothing to build, lint, or test. When the first real code lands, replace this section with the actual commands (build, lint, test, run single test) and architecture overview.

## Repository layout

- `.agents/skills/hindsight-docs/` — Hindsight documentation skill (offline docs pack: architecture, APIs, config). `.claude/skills/hindsight-docs` is a symlink to it and `skills-lock.json` pins installed versions. Both are managed by `npx skills ...` — don't edit in place.
- `docs/agents/` — tooling conventions consumed by the mattpocock-skills engineering skills (`to-tickets`, `triage`, `to-spec`, `wayfinder`, domain-docs rules). Edit directly when conventions change.
- `.omc/` — oh-my-claudecode runtime state (session-local operational artifacts; never commit; `.omc/skills/` is the exception for committed project skills).
- `.codegraph/` — codegraph symbol index. Empty until code exists; refresh with `codegraph sync` after adding files.

## Agent skills

### Issue tracker

Issues are tracked as local markdown files under `.scratch/<feature>/`. See `docs/agents/issue-tracker.md`.

### Triage labels

Default five-role vocabulary, label strings unchanged: `needs-triage`, `needs-info`, `ready-for-agent`, `ready-for-human`, `wontfix`. See `docs/agents/triage-labels.md`.

### Domain docs

Single-context: root `GLOSSARY.md` + `docs/adr/`, created lazily by `/domain-modeling`. See `docs/agents/domain.md`.
