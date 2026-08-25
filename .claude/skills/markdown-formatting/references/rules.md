# markdownlint rules

Loaded from the `markdown-formatting` skill, whose body is `../SKILL.md`.

The rule numbers behind each section of the skill, so a linter message can be traced back to the reason. Full text: <https://github.com/DavidAnson/markdownlint/blob/main/doc/Rules.md>.

## Contents

- [Headings](#headings)
- [Lists](#lists)
- [Whitespace and blank lines](#whitespace-and-blank-lines)
- [Code](#code)
- [Links and images](#links-and-images)
- [Emphasis](#emphasis)
- [Tables](#tables)
- [File level](#file-level)
- [Rules this skill turns off](#rules-this-skill-turns-off)

## Headings

| Rule | Alias | Catches |
| --- | --- | --- |
| MD001 | heading-increment | A level skipped, such as `##` followed by `####` |
| MD003 | heading-style | ATX mixed with setext underlines |
| MD018 | no-missing-space-atx | `##Heading` with no space |
| MD019 | no-multiple-space-atx | `##  Heading` with two spaces |
| MD020 | no-missing-space-closed-atx | `##Heading##` |
| MD021 | no-multiple-space-closed-atx | Extra spaces inside closed hashes |
| MD022 | blanks-around-headings | A heading glued to the text above or below |
| MD023 | heading-start-left | An indented heading, which is not a heading |
| MD024 | no-duplicate-heading | Two identical headings, so two identical anchors |
| MD025 | single-h1 | A second `#` in one document |
| MD026 | no-trailing-punctuation | `## Setup:` |
| MD036 | no-emphasis-as-heading | A bold line standing in for a heading |
| MD041 | first-line-h1 | The document does not open with `#` |
| MD043 | required-headings | The heading set does not match a configured structure |

## Lists

| Rule | Alias | Catches |
| --- | --- | --- |
| MD004 | ul-style | `-` and `*` mixed as markers |
| MD005 | list-indent | Items of one level indented differently |
| MD007 | ul-indent | Nested list not indented by the configured width |
| MD029 | ol-prefix | Ordered list numbering that breaks the configured style |
| MD030 | list-marker-space | Wrong number of spaces after the marker |
| MD032 | blanks-around-lists | A list glued to the paragraph before or after |

## Whitespace and blank lines

| Rule | Alias | Catches |
| --- | --- | --- |
| MD009 | no-trailing-spaces | Trailing whitespace that is not a deliberate line break |
| MD010 | no-hard-tabs | A tab character anywhere |
| MD012 | no-multiple-blanks | Two or more blank lines in a row |
| MD027 | no-multiple-space-blockquote | Extra spaces after `>` |
| MD028 | no-blanks-blockquote | A blank line splitting one quote into two |
| MD035 | hr-style | Horizontal rules written two ways |
| MD047 | single-trailing-newline | The file does not end with exactly one newline |

## Code

| Rule | Alias | Catches |
| --- | --- | --- |
| MD031 | blanks-around-fences | A fence glued to the text around it |
| MD038 | no-space-in-code | `` ` code ` `` with padding spaces |
| MD040 | fenced-code-language | A fence with no language |
| MD046 | code-block-style | Fenced and indented blocks mixed |
| MD048 | code-fence-style | Backtick and tilde fences mixed |
| MD014 | commands-show-output | `$` before a command whose output is not shown |

## Links and images

| Rule | Alias | Catches |
| --- | --- | --- |
| MD011 | no-reversed-links | `(text)[url]` |
| MD034 | no-bare-urls | A URL in prose without angle brackets |
| MD039 | no-space-in-links | `[ text ](url)` |
| MD042 | no-empty-links | A link with no destination |
| MD045 | no-alt-text | An image with no alt text |
| MD051 | link-fragments | An anchor link pointing at no heading |
| MD052 | reference-links-images | A reference label that was never defined |
| MD053 | link-image-reference-definitions | A definition nothing references |
| MD054 | link-image-style | Inline and reference styles mixed |
| MD059 | descriptive-link-text | `click here` and its relatives |

## Emphasis

| Rule | Alias | Catches |
| --- | --- | --- |
| MD037 | no-space-in-emphasis | `** text **`, which does not render as bold |
| MD049 | emphasis-style | `_italic_` and `*italic*` mixed |
| MD050 | strong-style | `**bold**` and `__bold__` mixed |

## Tables

| Rule | Alias | Catches |
| --- | --- | --- |
| MD055 | table-pipe-style | Leading and trailing pipes used inconsistently |
| MD056 | table-column-count | A row with more or fewer cells than the header |
| MD058 | blanks-around-tables | A table glued to the text around it |
| MD060 | table-column-style | Column alignment that breaks the configured style |

MD056 is the one that loses data silently. Treat it as an error, never a warning.

## File level

| Rule | Alias | Catches |
| --- | --- | --- |
| MD033 | no-inline-html | Raw HTML beyond the allowed set |
| MD044 | proper-names | A product name capitalised the wrong way |

## Rules this skill turns off

| Rule | Why |
| --- | --- |
| MD013 line-length | Prose here is unwrapped, one paragraph per line. A rule that fails on every line trains everyone to ignore the linter. |
