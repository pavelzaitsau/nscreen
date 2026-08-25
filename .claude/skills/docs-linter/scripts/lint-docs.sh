#!/usr/bin/env bash
# Docs style gate. Run locally, or from CI where no action is available.
# Vale must already be on PATH; this script does not install it.
set -euo pipefail
if ! command -v vale >/dev/null 2>&1; then
  echo "vale not found; install it (brew install vale, or see valelint.github.io)" >&2
  exit 127
fi
vale --config=.vale.ini --minAlertLevel=error "${1:-docs/}"
