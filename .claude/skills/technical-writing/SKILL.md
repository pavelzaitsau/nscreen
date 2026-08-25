---
name: technical-writing
description: "Write documentation a reader understands on the first pass - READMEs, runbooks, ADRs, requirements, doc comments, inline code comments and commit bodies. Covers where a fact belongs, the shape of the document, sentence-level rules, comment discipline and vocabulary. Use this skill whenever text is going into a file: writing a new document, rewriting one that reads vague or long-winded, reviewing someone else's prose, or deciding whether a code comment earns its place. Trigger it even when the user only says 'write it up', 'make this clearer' or 'add a comment' without naming documentation. Do NOT use it for chat replies, marketing copy, or the Markdown syntax itself, which belongs to the markdown-formatting skill."
license: Complete terms in LICENSE.txt
---

# Technical writing

The goal is a document a reader understands on the first pass, without asking anyone a question. A rule that does not serve that goal is ceremony, and this skill does not carry it.

The sentence rules are a reduced subset of ASD-STE100, Simplified Technical English.

This skill is not normative. It gives plain imperatives. Uppercase RFC 2119 keywords appear here only where the text quotes a rule about them.

## Scope

| Applies to | Does not apply to |
| --- | --- |
| Documentation, READMEs, runbooks | Chat replies |
| Specifications, requirements, ADRs | Marketing copy |
| Doc comments and docstrings | Quotations, reproduced verbatim |
| Inline code comments | Code, config and DSL inside fenced blocks |
| Commit bodies | Generated files |

Rule 5 governs normative text only: specifications, requirements, contracts, ADRs. A README that says `MUST` at its reader sounds like a standards body. Elsewhere, state the fact.

## Reader first

Five decisions, made before the first sentence. They matter more than every rule below.

1. **Name the reader.** A new joiner, an operator at 03:00 and an integrator need different documents. Writing for all three produces a document that serves none.
2. **Conclusion first.** The answer, then the justification, then the detail. A reader who stops after two sentences still leaves with the point.
3. **Assume arrival from a search result.** Nobody reads a documentation set from page one. Each section states its own context in one line and never says "as described above".
4. **An example beats a definition.** One worked example with real values replaces a paragraph of abstraction. Give the example first and generalise after it. Rules for writing examples are in `references/examples.md`.
5. **One screen per section.** A section that does not fit a screen holds two topics. Split it, or the reader skims and misses one of them.

Define a term at its first use, once. Re-explaining a term signals that the first definition failed, and the reader then has to work out which explanation is current.

## One home per fact

Every fact has exactly one owning file. A fact repeated in two files is not redundancy, it is a future contradiction: someone updates one copy, and everyone keeps reading the other.

Where duplication is unavoidable, the copy says that it is a copy and names the owner.

| What appeared | Where it goes |
| --- | --- |
| A decision with real alternatives | An ADR |
| A decision with no alternative | The component's doc, as a plain statement of fact |
| A behaviour the system must have | The requirements document |
| A fact about how a component works | That component's doc |
| A gap between the specification and reality | A named "known deviations" section, never a code comment |
| A question with no answer yet | An open questions table |
| A procedure an operator runs | A runbook |
| A rule a machine can check | A linter rule |

The last row is the one people skip. Where a linter enforces a rule, prose states the intent once and never repeats the word list.

Fixed forms for the recurring documents - README, runbook, ADR, requirement, open question - are in `references/templates.md`. Pick the template that matches. Do not invent a structure, and do not add sections. An empty required section means the work is not done, not that the section is inapplicable.

## The twelve rules

1. **Active voice.** The subject performs the action.
   - No: `The message is retried after a failure.`
   - Yes: `The worker retries the message after a failure.`
   - Two exceptions: a definition, and a statement about the document itself. Everywhere else, name the actor. If naming it is hard, the design is unclear, not the sentence.

2. **One fact per sentence.** Split on `and`, `but`, `which`, `;` whenever both halves stand alone.

3. **Sentence length.** Max 25 words in descriptive text. Max 20 words in requirements and procedures.

4. **Present tense for behaviour.**
   - No: `The client will open a connection.`
   - Yes: `The client opens a connection.`

5. **RFC 2119 for obligation, uppercase, nothing else.** `MUST`, `MUST NOT`, `SHOULD`, `SHOULD NOT`, `MAY`. Normative text only.
   - No: `It is important that the caller does not reuse the token.`
   - Yes: `The caller MUST NOT reuse the token.`

6. **No hedging.** An estimate is a number with a scope: `~200 ms at the 95th percentile`. The banned list lives in `references/vocabulary.md`.

7. **No hidden verbs.**
   - No: `performs validation of`, `provides support for`, `makes use of`
   - Yes: `validates`, `supports`, `uses`

8. **No orphan pronouns.** `this`, `it` and `that` are followed by the noun they refer to, or replaced by it.
   - No: `This breaks the retry.`
   - Yes: `A timeout shorter than the backoff breaks the retry.`

9. **Measurements are digits, with unit and scope.** `timeout = 30 s per attempt`, `batch size = 500 rows`. A small count inside a sentence may stay a word.

10. **Keep articles and copulas.** Telegraphic style has no place in a file. Articles separate `a connection` from `the connection`, and a dropped `is not` is how negation gets lost.

11. **Shape follows content.**
    - 2+ dimensions of fact -> table
    - 3+ parallel items -> list
    - causal reasoning -> prose, max 5 lines per paragraph
    - control flow or state -> a text diagram, inline. Rules for diagrams are in `references/templates.md`.

12. **Say what breaks.** A constraint carries its consequence in one sentence.
    - `The timeout MUST exceed the backoff. Below it, every retry is cancelled before it reaches the server.`

Three habits to drop, with no rule number needed. Delete rhetorical setup: `It is worth noting that the order matters.` becomes `The order matters.` Turn a prose enumeration into a numbered list. Never apologise for the text in the text; fix the section instead.

## Comments in code

The default is no comment. Code states what happens. A comment earns its place only by stating something the code cannot state.

| A comment carries | Content |
| --- | --- |
| Why, not what | The reason a non-obvious branch exists |
| An external constraint | A rate limit, a vendor defect, a retention period, a protocol quirk |
| A rejected alternative | The obvious simpler version, and why it fails here |
| A dangerous invariant | What breaks if two calls are reordered, or a lock is dropped |
| A reference | The ticket, RFC or ADR that explains the oddity |

```text
// The vendor returns 200 with an empty body on a throttled request.
// Treating it as success loses the page silently, so an empty body
// is retried. Removing this check reintroduces defect #4812.
```

Nothing in those three lines is visible in the code.

| Noise, delete on sight | Why |
| --- | --- |
| Restating the signature | The signature is already there, and it stays correct |
| Restating the next line | A comment saying `increment the counter` above the increment |
| Section banners | A file that needs internal banners is two files |
| Commented-out code | Version control already keeps it, and dead code in a comment rots and pollutes grep |
| Author and date | The version control system knows both, and knows them correctly |
| A comment compensating for a bad name | Rename the thing instead |
| A comment restating a test's assertions | The assertions are the specification |

**A comment that contradicts the code is worse than no comment.** The reader trusts it; no tool checks it. When you change a line, read the comment above it in the same edit, then update or delete it there.

**Doc comments on a public API** are written for a caller who cannot see the body. State what the member does in one sentence under 20 words. Document units, ranges, null handling and ownership for a parameter whose name does not already say them. State what an empty, absent or zero result means. Name the errors the caller must handle and the condition that raises each. State thread safety, blocking and side effects, or the caller assumes none. Never restate types; the signature carries them.

**TODO discipline.** `TODO(owner, ticket): <the action>, blocked on <condition>`. No condition means do it now or delete it.

## Commit bodies

The subject is imperative, under 50 characters, with no trailing period. The body says why the change was made; the diff already says what changed. Machine-readable lines - metrics, benchmark numbers, issue references - are data, not prose, and keep their own format.

## Review checklist

The mechanical checks belong to a linter. These six need a human.

- [ ] The first paragraph answers the question the reader arrived with
- [ ] Every section states its own context and survives being read alone
- [ ] Every constraint states its consequence
- [ ] Every claim that could be wrong carries its source or a number
- [ ] Every comment near a changed line still matches that line
- [ ] Read the first sentence of every section in order: they alone tell the story

Then hand the document to someone outside the team and count their questions. Each question is a defect, and it has a location.

Vocabulary lookups are in `references/vocabulary.md`. The machine-checkable half of these rules is the `docs-linter` skill.
