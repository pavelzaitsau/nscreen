# Examples

Loaded from the `technical-writing` skill, whose body is `../SKILL.md`. Reader-first decision 4 says an example beats a definition. This file says how to write one, and shows one section rewritten.

## Rules for a code example

1. **Real values, never placeholders.** `--region eu-central-1` teaches; `--region <YOUR_REGION>` makes the reader stop and guess. Where a value must be secret, use a clearly fake but well-formed one, and say so once.
2. **Runnable as written.** A reader copies the block. Include the imports, the setup line and the arguments. An example that needs three invisible prerequisites is a puzzle.
3. **Show the output.** Half the value of an example is the reader recognising a match. Paste the real output, trimmed, and mark the trim.
4. **Show one failure.** The success path is the easy half. One example of the common error, with the message the reader will actually see, saves the support ticket.
5. **One idea per example.** An example that demonstrates three features teaches none of them. Split it.
6. **Smallest thing that still works.** Delete every line the point does not need, then check it still runs.
7. **Protect it from rotting.** An example in a doc is untested code. Extract it into a test, generate the doc from the test, or add it to the release checklist. Choose one, and say which in the contributor guide.
8. **Never invent output.** A pasted result that nobody ran is the fastest way to lose a reader's trust for the whole document.

## Worked example: one section, before and after

### Before

```markdown
## Caching

The caching layer is utilized in order to provide support for reducing
the load on the database. It is important that the TTL is configured
correctly, as it can basically cause issues if it is set too low. In
general, values are cached for a few minutes, though this may vary. The
cache is populated by the service on a read. Note that it should
probably be noted that invalidation is handled automatically in most
cases, but there are some situations where this is not the case and
manual intervention may be required.

Nine defects in eight lines: hidden verbs (`is utilized in order to provide support for`), filler (`basically`, `It is important that`), hedges (`a few minutes`, `may vary`, `in general`, `should probably`), an orphan `this` twice, agentless passive throughout, a 38-word closing sentence, a vague obligation with no actor, no number anywhere, and no statement of what breaks.

### After

## Caching

The read path caches query results, which cuts database load by roughly
80% on the hot path.

The service writes to the cache on every read miss. Entries expire after
`CACHE_TTL`, default 300 s.

A TTL below 30 s costs more than it saves: the miss rate passes 50%, and
every miss adds a round trip.

The service invalidates an entry when it writes the underlying row. Two
cases need a manual `cache-flush`:

- A row changed by a migration, because migrations bypass the service.
- A row changed by another service that shares the database.

What changed, and why each change matters:

| Change | Effect on the reader |
| --- | --- |
| Conclusion moved to the first line, with a number | Answers "should I care" before the detail |
| `a few minutes` became `300 s`, named `CACHE_TTL` | The reader can now check the value |
| The TTL warning states what breaks, with a threshold | Turns advice into a decision rule |
| `handled automatically in most cases` became two named cases | The exception was the only actionable part |
| Actors named: the service writes, the service invalidates | The reader knows what to look at in the code |
| 38-word sentence split into a lead line and a list | Skimmable, and each case survives being read alone |

The rewrite is shorter and carries four facts the original did not: the load reduction, the setting name, the default, and the threshold. Clarity added information; it did not trade it away.
```
