"""Machine-checkable subset of the markdown-formatting and technical-writing skills.

Run from the repository root: python .claude/audit-docs.py
Prose rules that need judgement (active voice, one fact per sentence, whether a comment earns
its place) are not here; the skills' own review checklists cover those.
"""
import pathlib
import re
import sys

SKIP_PARTS = {".git", "bin", "obj", "publish", ".vs"}
# The imported skills are upstream artifacts with their own licence: audited, never rewritten.
UPSTREAM = ".claude/skills/"

BANNED = [
    (r"\bbasically\b", "filler"),
    (r"\bessentially\b", "filler"),
    (r"\bsimply\b", "filler, and it insults a reader who is stuck"),
    (r"\bjust\b", 'filler; write "only" or "newly" where that is the meaning'),
    (r"\bobviously\b", "if it were obvious the sentence would be unnecessary"),
    (r"\bof course\b", "same as obviously"),
    (r"\bas you know\b", "same as obviously"),
    (r"should (be fine|work)\b", "an untested claim wearing a hedge"),
    (r"\betc\.", "an open list specifies nothing"),
    (r"\band so on\b", "an open list specifies nothing"),
    (r"\bTBD\b", "no owner"),
]

PREFER = [
    (r"\butiliz(e|es|ed|ing)\b", "use"),
    (r"\bleverag(e|es|ed|ing)\b", "use"),
    (r"\binitiat(e|es|ed|ing)\b", "start"),
    (r"\bterminat(e|es|ed|ing)\b", "stop"),
    (r"\bpurg(e|es|ed|ing)\b", "delete"),
    (r"\bverif(y|ies|ied)\b", "check"),
    (r"\bensur(e|es|ed|ing)\b", "check"),
    (r"\btransmit(s|ted)?\b", "send"),
    (r"\bprior to\b", "before"),
    (r"\bsubsequent to\b", "after"),
    (r"\bdue to the fact that\b", "because"),
    (r"\bin order to\b", "to"),
    (r"\bapproximately\b", "about"),
    (r"\bat this point in time\b", "now"),
    (r"\bin the event that\b", "if"),
    (r"\bis able to\b", "can"),
    (r"\bhas the ability to\b", "can"),
    (r"\bperforms \w+ of\b", "the verb itself"),
    (r"\bprovides support for\b", "supports"),
    (r"\bmakes use of\b", "uses"),
]

MAX_SENTENCE_WORDS = 25


def files():
    for path in sorted(pathlib.Path(".").rglob("*")):
        if path.is_file() and not any(part in SKIP_PARTS for part in path.parts):
            yield path


def prose_lines(path, text):
    """Yield (line number, line) for prose only: outside fenced code, outside tables."""
    fenced = False
    for number, line in enumerate(text.splitlines(), 1):
        if line.lstrip().startswith("```"):
            fenced = not fenced
            continue
        if fenced or line.lstrip().startswith("|"):
            continue
        yield number, line


def comment_lines(text):
    for number, line in enumerate(text.splitlines(), 1):
        stripped = line.strip()
        if stripped.startswith(("//", "///", "#", "<!--", "*")) or "<!--" in stripped:
            yield number, stripped


def check_markdown(path, text, out):
    lines = text.splitlines()

    if not lines or not lines[0].startswith("# "):
        out(path, 1, "MD041", "first line is not a level-1 heading")

    h1 = [n for n, line in enumerate(lines, 1) if line.startswith("# ")]
    if len(h1) > 1:
        out(path, h1[1], "MD025", f"{len(h1)} level-1 headings; a document has one title")

    previous = 0
    fenced = False
    for number, line in enumerate(lines, 1):
        if line.lstrip().startswith("```"):
            fenced = not fenced
            if not fenced:
                continue
            language = line.lstrip()[3:].strip()
            if not language:
                out(path, number, "MD040", "fenced block names no language")
            if number > 1 and lines[number - 2].strip():
                out(path, number, "MD031", "no blank line before the fence")
            continue
        if fenced:
            continue

        match = re.match(r"^(#{1,6}) (.*)", line)
        if match:
            level, title = len(match.group(1)), match.group(2)
            if previous and level > previous + 1:
                out(path, number, "MD001", f"heading jumps from h{previous} to h{level}")
            previous = level
            if title.rstrip().endswith((".", ",", ";", ":", "!", "?")):
                out(path, number, "MD026", "heading ends with punctuation")
            if number > 1 and lines[number - 2].strip():
                out(path, number, "MD022", "no blank line before the heading")
            if number < len(lines) and lines[number].strip():
                out(path, number, "MD022", "no blank line after the heading")

        if "\t" in line:
            out(path, number, "MD010", "hard tab")
        if line.rstrip() != line and line != line.rstrip() + "  ":
            out(path, number, "MD009", "trailing whitespace")
        if re.search(r"(?<![(<\[`\w])https?://\S+", line) and not re.search(r"[(<]https?://", line):
            out(path, number, "MD034", "bare URL; wrap it in angle brackets or make it a link")
        if re.search(r"!\[\]\(", line):
            out(path, number, "MD045", "image without alt text")

    for number, line in enumerate(lines, 1):
        if number >= 3 and not line.strip() and not lines[number - 2].strip():
            out(path, number, "MD012", "two consecutive blank lines")

    markers = {re.match(r"^\s*([-*+]) ", line).group(1)
               for line in lines if re.match(r"^\s*([-*+]) ", line)}
    if len(markers) > 1:
        out(path, 1, "MD004", f"mixed unordered list markers: {sorted(markers)}")

    if not text.endswith("\n") or text.endswith("\n\n"):
        out(path, len(lines), "MD047", "file does not end with exactly one newline")

    # Table cell counts, per contiguous block of pipe rows. Fences are skipped: an ASCII diagram
    # inside one is full of pipes and is not a table.
    block, start, fenced = [], 0, False
    for number, line in enumerate(lines + [""], 1):
        if line.lstrip().startswith("```"):
            fenced = not fenced
            continue
        if not fenced and line.lstrip().startswith("|"):
            if not block:
                start = number
            block.append(line)
            continue
        if block:
            counts = {len(row.strip().strip("|").split("|")) for row in block}
            if len(counts) > 1:
                out(path, start, "MD056", f"table rows have different cell counts: {sorted(counts)}")
            block = []


def check_prose(path, text, out, markdown):
    source = prose_lines(path, text) if markdown else comment_lines(text)
    for number, line in source:
        # Inline code is code, not prose: the skill's scope excludes it, and a document that names
        # a banned word as a token - `we`, `just` - is quoting the rule, not breaking it.
        lowered = re.sub(r"`[^`]*`", "", line).lower()
        for pattern, reason in BANNED:
            for m in re.finditer(pattern, lowered):
                out(path, number, "banned", f'"{m.group(0)}" - {reason}')
        for pattern, better in PREFER:
            for m in re.finditer(pattern, lowered):
                out(path, number, "prefer", f'"{m.group(0)}" -> "{better}"')
        if re.search(r"\bwe\b|\bour\b|\bus\b", lowered):
            out(path, number, "actor", '"we/our/us" - name the actor instead')

    if not markdown:
        return
    for number, line in prose_lines(path, text):
        stripped = line.strip()
        if not stripped or stripped.startswith(("#", "-", "*", ">", "|")) or re.match(r"^\d+\.", stripped):
            continue
        for sentence in re.split(r"(?<=[.!?]) +", stripped):
            words = len(sentence.split())
            if words > MAX_SENTENCE_WORDS:
                out(path, number, "length", f"{words} words in one sentence, max {MAX_SENTENCE_WORDS}")


def main():
    findings = []

    def out(path, line, rule, message):
        findings.append((str(path), line, rule, message))

    for path in files():
        try:
            text = path.read_text(encoding="utf-8")
        except (UnicodeDecodeError, OSError):
            continue
        if str(path).replace("\\", "/").startswith(UPSTREAM):
            continue
        if path.suffix == ".md":
            check_markdown(path, text, out)
            check_prose(path, text, out, markdown=True)
        elif path.suffix in {".cs", ".ps1", ".props", ".csproj", ".slnx", ".manifest", ".js"}:
            check_prose(path, text, out, markdown=False)

    by_rule = {}
    for _, _, rule, _ in findings:
        by_rule[rule] = by_rule.get(rule, 0) + 1

    for path, line, rule, message in findings:
        print(f"{path}:{line}: {rule}: {message}")

    print(f"\n{len(findings)} finding(s)" + (f": {by_rule}" if findings else ""))
    return 1 if findings else 0


if __name__ == "__main__":
    sys.exit(main())
