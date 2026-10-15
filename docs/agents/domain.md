# Domain Docs

## Before exploring

Read root `CONTEXT.md` and relevant decisions under `docs/adr/`.
If `CONTEXT-MAP.md` exists later, follow its pointers for the area involved.
Proceed silently when these files are absent.

## Layout

This repository uses one context:

- `CONTEXT.md`: domain terminology and context.
- `docs/adr/NNNN-descriptive-title.md`: architectural decisions.

Create domain documents through domain-modeling when terminology or
decisions are resolved; empty scaffolding is unnecessary.

## Vocabulary and decisions

Use the glossary’s terms in specifications, issues, code proposals,
and tests. Flag meaningful terminology gaps for domain-modeling.

When a proposal conflicts with an ADR, identify the decision and explain
why it should be reconsidered.
