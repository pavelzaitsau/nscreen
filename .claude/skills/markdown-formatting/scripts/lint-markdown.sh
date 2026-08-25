#!/usr/bin/env bash
# Markdown structure gate. Prose style is a separate tool; see the docs-linter skill.
# Usage: scripts/lint-markdown.sh [path ...]   (default: every .md below the working directory)
set -euo pipefail

here=$(cd "$(dirname "$0")/.." && pwd)
config="$here/assets/.markdownlint.jsonc"

if ! command -v markdownlint-cli2 >/dev/null 2>&1; then
  echo "markdownlint-cli2 not found; install it with: npm install -g markdownlint-cli2" >&2
  exit 127
fi

globs=()
if [ "$#" -eq 0 ]; then
  globs=("**/*.md")
else
  for t in "$@"; do
    if [ -d "$t" ]; then globs+=("${t%/}/**/*.md"); else globs+=("$t"); fi
  done
fi

markdownlint-cli2 --config "$config" "${globs[@]}"
