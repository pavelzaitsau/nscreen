---
name: docs-linter
description: "Set up and tune a Vale style gate for a documentation set: install, pre-commit and CI wiring, ready rule files for hedging, filler, vague obligation and abbreviations, and the config traps that cost an afternoon each. Use this skill whenever documentation style needs enforcing rather than advising - adding the linter to a repository, writing or tuning a rule, or working out why a rule fired or stayed silent. Trigger it when someone asks to 'check the docs automatically', 'stop people writing X' or 'add a style check to CI'. Do NOT use it for the writing rules themselves, which live in technical-writing, or for Markdown structure, which is markdown-formatting."
license: Complete terms in LICENSE.txt
compatibility: "Vale on PATH (brew install vale); the rule files and traps apply to any Vale version from 2.x"
---

# Docs style gate

A convention nobody checks decays within a quarter. Everything a linter can express belongs to the linter, and stops being prose.

The writing rules this gate enforces are in the `technical-writing` skill. This skill covers the machine.

A convention nobody checks decays within a quarter. Everything a linter can express belongs to the linter, and stops being prose.

| Enforceable by a linter | Not enforceable, stays in a skill |
| --- | --- |
| Banned words and hedges | Whether a section is complete |
| Vague obligation instead of RFC 2119 | Whether an ADR had real alternatives |
| Sentence length | Whether a fact is in the right file |
| Abbreviations in prose | Whether a comment states why |
| Terminology substitutions | Whether a number is true |

The shipped config in `assets/vale/` is a starting point, not a standard. The shipped config avoids every trap listed below.

## Install

Vale is a Go binary, not a package of your language's ecosystem. Install it separately, or the hook fails to spawn instead of passing silently.

```bash
cp -r assets/vale/.vale.ini assets/vale/styles .
vale --minAlertLevel=error <docs-root>/
```

## Wiring

Pre-commit, in the `local` repo block:

```yaml
      - id: vale-docs
        name: docs style (Vale)
        entry: vale --minAlertLevel=error
        language: system
        files: ^<docs-root>/.*\.md$
```

CI, as a step after the code linter:

```yaml
      - name: docs
        uses: errata-ai/vale-action@reviewdog
        with:
          files: <docs-root>
          fail_on_error: true
```

`scripts/lint-docs.sh` does the same without the action, and exits 127 with an install hint when Vale is absent.

`Docs.Rfc2119` is a warning, not an error, and deliberately so. RFC 2119 keywords belong in normative text only, and the gate cannot tell a specification from a README. An error-level rule here would force `MUST` into every guide.

Errors block. Warnings do not. Put a rule at `error` only when a false positive is worse than the thing it catches never being written.

## Shipped rules

| Rule | Level | Catches |
| --- | --- | --- |
| `Docs.Hedging` | error | `probably`, `a few`, `tends to`, `in general`, `it seems` |
| `Docs.Filler` | error | `basically`, `essentially`, `obviously`, `simply` |
| `Docs.Rfc2119` | warning | `needs to be`, `we should`, `it is important that` |
| `Docs.NoAbbrev` | error | `cfg`, `pkg`, `msg`, `req`, `resp`, `idx` in prose |
| `Docs.HiddenVerbs` | warning | `performs validation of`, `utilize`, `in order to`, `prior to` |
| `Docs.Passive` | warning | agentless passive |
| `Docs.SentenceLength` | warning | a sentence over 25 words |
| `Docs.Rfc2119Case` | warning | lowercase `must` / `should` where an obligation is meant |
| `Docs.Terms` | error | project terminology, empty until the project fills it |
| `Vale.Terms` | error | wrong capitalisation of a proper noun |
| `Vale.Avoid` | error | words listed in `reject.txt` |

## Config traps

Each one costs an afternoon to find.

**`Vale` must be listed in `BasedOnStyles`.** The vocabulary files under `styles/config/vocabularies/` do nothing unless the built-in `Vale` style is enabled. With a project style alone they are dead config that looks like it works.

**`raw` concatenates, `tokens` alternates.** A list under `raw:` is joined into one regular expression, not OR-ed. Two or more alternatives under `raw:` silently produce a pattern that never matches. Use `tokens:` for alternatives.

**Vale uses RE2.** No lookahead, no lookbehind. Express every exclusion as a positive pattern.

**Substitutions do not match across a line break.** A phrase wrapped by the formatter passes the rule. Reflow the paragraph if the rule matters.

**Ignore scopes decide the whole design.** Fenced blocks are excluded by `BlockIgnores`, inline code spans by `TokenIgnores`. Ignore scopes are why a style rule MUST cite the wording it bans inside backticks - otherwise the rule text trips its own rule.

**Spell check is off by design.** `Vale.Spelling = NO`. On a technical corpus it produces more noise than every other rule combined.

## Adding a rule

1. Write the rule under `styles/Docs/`.
2. Write a probe file containing the wording the rule must catch.
3. Write a probe file containing legitimate domain text the rule must NOT catch. **This step is not optional.** A `Filler` rule that bans `just` also flags `just-in-time`, and only the false-positive probe exposes it.
4. Run both probes at `--minAlertLevel=warning`.
5. If the rule encodes terminology, generate it from the project's term list. Never maintain the list and the rule by hand: two hand-synchronised copies drift, and the drift is invisible until someone greps.

## What is deliberately not linted

- Any word that is also a legitimate domain term. List it in the free-words section of the project's term list instead.
- Chat and review comments. The gate covers files.
- Anything a reviewer must judge: completeness, ordering, whether a claim is true.

## Adapting to a project

Fill these four and nothing else. The paths are the shipped ones; after the install step above, the same files sit under `styles/` at the repository root.

1. The routing table, in the contributor guide, with real paths.
2. `assets/vale/styles/Docs/Terms.yml` - the project's terminology substitutions, generated from the term list.
3. `assets/vale/styles/config/vocabularies/Project/accept.txt` - proper nouns and product names, so `Vale.Terms` stops flagging them.
4. `assets/vale/styles/config/vocabularies/Project/reject.txt` - the misspellings this project actually produces.

The docs root, the severity of each rule and the `NoAbbrev` word list are also project-tunable. Everything else should survive the move to a new repository unchanged.
