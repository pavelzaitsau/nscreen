---
name: commit-messages
description: "Write a commit message that follows Conventional Commits 1.0.0: type, optional scope, imperative subject, ticket id, optional body, footers, and breaking-change marking. Use this skill whenever a commit is being prepared, amended, squashed or reviewed, and whenever a message needs checking against the convention. Trigger it even when the user only says 'commit this', 'what should the message be' or 'clean up these commits'. Do NOT use it for pull request descriptions, changelogs or release notes."
license: Complete terms in LICENSE.txt
---

# Commit messages

```text
<type>[optional scope][!]: [TICKET-ID] <description>

[optional body]

[optional footer(s)]
```

English, imperative, plain.

## Cut everything that carries no fact

Shorter wins at every level, and stops exactly where a fact would go.

- No body where the subject already says it.
- No scope where the change spans areas, or where a ticket id already names the place.
- No footer that repeats what the subject or the body states.
- No word whose removal changes nothing: `properly`, `correctly`, `some`, `various`, `also`, `now`.

Articles and negation stay. `a retry` and `the retry` are different claims, and a dropped `not` inverts one.

A subject a reader cannot act on is not shorter, it is empty.

## One commit, one change

A change that fits two types is two commits. Formatting never travels with logic, and a mass rename travels alone.

Every commit builds and passes its tests on its own. The cost of breaking this shows up once, in `git bisect`, and by then the history is already useless.

## Subject

The subject stands alone and carries the whole change for a reader of `git log --oneline`.

- 50 characters or fewer, including type and scope. Over 72 is rejected.
- Lower case after the colon. No trailing period.
- Imperative mood: `add`, not `added` or `adds`.
- Say what the commit does, not what the author did.

| Weak | Strong |
| --- | --- |
| `update parser` | `accept a trailing comma in a list` |
| `fix bug` | `stop a retry from firing after a timeout` |
| `refactor code` | `move date parsing out of the request handler` |
| `improve performance` | `cache the compiled pattern between calls` |

A subject a reader cannot act on is not shorter, it is empty.

| Type | When |
| --- | --- |
| `feat` | new behaviour a user can observe |
| `fix` | a defect in existing behaviour |
| `perf` | same behaviour, less time or memory |
| `refactor` | structure changes, behaviour does not |
| `test` | tests only |
| `docs` | documentation, comments, agent instructions |
| `build` | dependencies, lockfiles, packaging, Docker |
| `ci` | pipelines, hooks, build targets |
| `chore` | anything the other types do not cover |
| `revert` | undoes an earlier commit |

A project may add types. It may not silently redefine these.

The type sets the version bump, which is why the choice is not a matter of taste.

| Marking | Release |
| --- | --- |
| `feat` | minor |
| `fix`, `perf` | patch |
| `!` or `BREAKING CHANGE:` | major |
| everything else | none |

**Scope** is optional: one lower-case noun in parentheses naming the area touched, no spaces. Omit it when the change spans several areas. Keep the project's scope list short and closed; an open list turns into free text within a month.

### Ticket id

Where the work has a ticket, its id goes in the subject, immediately after the colon, in the tracker's own casing.

```text
fix(auth): PROJ-482 reject a token that outlived its session
```

The id counts against the 50 characters. A ticket key plus a scope leaves little room, so drop the scope first: the ticket already says where the work belongs.

One id in the subject. Further ones go to a `Refs:` footer.

An id closes nothing by itself. A tracker that closes on commit needs its own footer, `Closes: #812` or the equivalent.

## Breaking change

Mark it twice: `!` before the colon, and a `BREAKING CHANGE:` footer saying what breaks.

```text
feat(api)!: return a cursor instead of an offset

BREAKING CHANGE: pagination callers must pass `cursor`; `offset` is gone.
```

`BREAKING CHANGE` is uppercase. `BREAKING-CHANGE` is a synonym. Everything else in the message is case-insensitive.

## Body

**Most commits have no body.** If the subject covers the change, stop there.

Write one only when the change needs a justification the subject cannot carry: a rejected alternative, an outside constraint, a number that drove the decision.

The body says why. The diff already says what. A body that restates the subject costs the reader time twice, and teaches them to skip the bodies that matter.

The specification calls the text after the colon the description, and it is mandatory. The optional part is the body.

**Do not wrap.** One paragraph per line, however long. A blank line separates paragraphs, and nothing else breaks a line. Wrapped bodies fight every tool that reflows them.

## Footers

Each footer is `Token: value` or `Token #value`, one per line, after a blank line. Tokens use hyphens instead of spaces, except `BREAKING CHANGE`.

Useful ones: `BREAKING CHANGE:`, `Refs:`, `Closes:`, `Reviewed-by:`. A project may define its own, such as a metric delta.

A revert names what it undoes:

```text
revert: drop the retry on a 429

Refs: 676104e
```

## Squash merge

A squash merge writes the pull request title into the history, not the commits on the branch. The title follows every rule above, or the convention leaks into `main` the first time someone merges.

The pull request body becomes the commit body: unwrapped, one paragraph per line.

Keep `Closes:` in the pull request body. In a branch commit it closes the ticket on merge, which is early.

## Enforcement

A rule nobody checks lasts until the first rush.

`assets/commitlint.config.js` covers structure: the type list, the case, the 72-character wall, the missing blank lines. It disables `body-max-line-length`, because the default fights the no-wrap rule.

`scripts/commit-msg` covers what commitlint cannot express: attribution lines, long dashes, and the lower-case start of the description after a ticket id. It warns past 50 characters and fails past 72.

The attribution check normalises each line first, so an indent or a leading emoji does not smuggle a line past it.

`Signed-off-by` is refused by default and is the one line worth keeping in a project that runs DCO. Set `ALLOW_SIGNOFF=1` there.

A coding agent adds its own attribution unless told otherwise. Turn that off in the agent's settings; leaving the hook to catch it every commit is a fight, not a gate.

`subject-case` is off on purpose. A ticket id is uppercase and fails every case rule commitlint offers, so the hook checks the word after the id instead.

## Never

| Banned | Why |
| --- | --- |
| Any line naming a tool or an assistant | `Co-Authored-By`, `Generated with`, `Claude-Session` and their kin are noise in `git log`, and they outlive their accuracy |
| A long dash | Hyphen only, everywhere |
| Trailing period in the subject | It buys nothing and eats a character |
| Past tense, or `I` | `git log` reads as a list of actions, not a diary |
| `update`, `fix stuff`, `wip`, `misc` | Says nothing a reader can act on |
| A wrapped body | Breaks in the wrong place in every viewer |
| Two types in one commit | Split it |

## Examples

```text
fix(query): drop a repeated keyword before it reaches BM25
```

```text
docs: correct the README and close two open questions
```

```text
feat(cache): serve a read from the cache on a miss

The read path hit the database twice for every miss: once to check, once to fetch. One call now does both, which removes about 40% of read load at peak.

Refs: #812
```

## Before committing

- [ ] One change, one type
- [ ] Subject under 50 characters, imperative, lower case, no period
- [ ] Ticket id in the subject where one exists
- [ ] Breaking change carries both `!` and the footer
- [ ] The body earns its place, or there is none
- [ ] The body, if present, is unwrapped and says why
- [ ] No attribution line, no long dash
