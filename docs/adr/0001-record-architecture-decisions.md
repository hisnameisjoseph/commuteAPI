# 1. Record architecture decisions

- **Status:** Accepted
- **Date:** 2026-11-12

## Context

This project is built partly to show how I make technical decisions, not just the code that results. Decisions made in a chat or a commit message are hard to find later.

## Decision

Every significant decision gets a short record in `docs/adr/`, numbered in order, using this format: context, decision, consequences. A record is never edited after it is accepted. A changed decision gets a new record that supersedes the old one.

## Consequences

- Reviewers can see why the system looks the way it does.
- Writing the record forces the trade-offs to be stated before the code is written.
