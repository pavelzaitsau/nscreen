# Document templates

Loaded from the `technical-writing` skill. Sentence rules stay in the skill body, `../SKILL.md`.

Pick the template that matches the document. Do not invent a structure, and do not add sections. An empty required section means the work is not done.

## README

The most-read and least-planned document in a repository. It answers four questions in this order, and stops.

```markdown
# <name>

<One sentence: what this does, for whom. No history, no motivation.>

## Use it

<The shortest path from nothing to a working result. Real values, not
placeholders. If it takes more than 10 lines, the quickstart is wrong.>

## What it does not do

<The three things a reader would reasonably assume and be wrong about.>

## Configuration

| Setting | Default | Effect |
|---|---|---|

## Troubleshooting

| Symptom | Cause | Fix |
|---|---|---|

## More

<Links to the deeper documents, one line each stating when to read them.>
```

`What it does not do` earns its place more than any other section. Most wasted hours start with a correct assumption about a neighbouring product.

Keep architecture, rationale and history out. A README that explains why the project exists has buried the command the reader came for.

## Runbook

Written for an operator at 03:00 who did not write the system and will not read a second document.

```markdown
# Runbook: <the symptom, as the alert phrases it>

- Trigger: <the alert, dashboard or report that sends someone here>
- Severity: <what is broken for whom while this is true>
- Owner: <team>
- Last verified: YYYY-MM-DD by <name>

## Before you start

<Access, tools and permissions needed. A step that fails on a missing
credential wastes the first five minutes.>

## Decide

<A short branch: which of the two or three causes this is, and how to
tell them apart in one command each.>

## Steps

1. <One action. One command in a block.>
   Expected: <what a healthy result looks like, verbatim>
   If not: <the branch, or the escalation>

## Verify

<The command that proves the incident is over. Not "check the dashboard".>

## Roll back

<How to undo every step above, in reverse. If a step cannot be undone,
say so at the step, not here.>

## Escalate

<Who, by which channel, and the three facts to hand them.>
```

Every step states its expected output. A step without one leaves the operator guessing whether it worked, and guessing at 03:00 is how a second incident starts.

`Last verified` is a date, not a promise. A runbook nobody has walked through in a year is a hypothesis.

## ADR

Write one only where real alternatives existed. Otherwise the text states a fact, and the component doc owns it.

Never edit an accepted ADR to change its argument. Write a new one and mark the old one superseded. Recording what was believed at the time is the point of the format; rewriting it destroys the only thing an ADR is for.

```markdown
# ADR-NNNN: <decision in 5 words or fewer>

- Status: proposed | accepted | superseded by ADR-MMMM
- Date: YYYY-MM-DD
- Supersedes: ADR-MMMM        <!-- omit if none -->

## Context

<= 8 lines. What forces the decision now. Numbers with units. Name the
requirement or the milestone it serves.

## Options considered

| # | Option | Cost | Why rejected / chosen |
|---|---|---|---|

## Decision

One sentence, bold.

## Consequences

| Direction | Consequence |
|---|---|
| Simpler | |
| Harder | |
| More expensive | |

## Risk

One paragraph. The specific way this decision gets silently undone later.

## Expected effect

The measurement that should move, and in which direction, or `none`.
State it before the change lands.
```

`Expected effect` is not ceremony. A prediction written before the measurement makes the measurement informative; a number read afterwards only confirms whatever happened.

## Requirement

```markdown
### R<N> <short name>

- Must: <the behaviour, one sentence, RFC 2119>
- Verified by: <test name, metric, or the manual procedure>
- Out of scope: <the nearest thing this is not>
```

A requirement with no verification line is a wish. Give it one or delete it.

`Out of scope` does more work than it looks. Most requirement disputes are about the boundary, not the behaviour.

## Open question

```markdown
| # | Question | Why it blocks | Owner | Needed by |
|---|---|---|---|---|
```

An open question without an owner and a date is a note. Delete it or assign it.

## Diagrams

Text sources only, inline: Mermaid, DOT or an equivalent. An exported image does not diff, a reviewer cannot comment on a line of it, and a reader on a terminal or a screen reader gets nothing from it.

Precede every diagram with one sentence stating what the reader should conclude from it.
