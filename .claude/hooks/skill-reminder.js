#!/usr/bin/env node
// A PreToolUse hook that names the skill to invoke at the moment it applies.
//
// Skills are model-invoked: an agent decides from the description list whether
// to load one. That decision degrades as the list grows, and a subagent whose
// tool list omits Skill cannot load one at all. This hook removes the decision
// for the four cases a tool call identifies exactly.
//
// Install: scripts/install-claude.sh
// Protocol: reads the hook payload on stdin, writes additionalContext on stdout.
// Exit code is always 0. A hook that fails must not block the tool call.

const fs = require('fs');
const os = require('os');
const path = require('path');

const EDIT_TOOLS = new Set(['Write', 'Edit', 'NotebookEdit']);

// A Vale style lives under styles/<Style>/<Rule>.yml next to a .vale.ini. The separator class
// accepts a backslash too: on Windows the tool reports file_path with backslashes, and a
// forward-slash-only pattern silently never matched there.
const VALE_CONFIG = /(^|[\\/])\.?vale\.ini$/i;
const VALE_RULE = /(^|[\\/])styles[\\/][^\\/]+[\\/][^\\/]+\.ya?ml$/i;

const RULES = [
  {
    id: 'markdown',
    applies: (tool, input) =>
      EDIT_TOOLS.has(tool) && /\.mdx?$/i.test(input.file_path || ''),
    context:
      'This is a Markdown file. Invoke the `markdown-formatting` skill for structure ' +
      '(blank lines around every block, heading levels, list markers, fenced code with ' +
      'a language, table cell counts) and the `technical-writing` skill for wording ' +
      '(where a fact belongs, sentence rules, vocabulary). Apply both to this edit and ' +
      'to every later Markdown edit in this session.',
  },
  {
    id: 'vale',
    applies: (tool, input) => {
      if (EDIT_TOOLS.has(tool)) {
        const file = input.file_path || '';
        return VALE_CONFIG.test(file) || VALE_RULE.test(file);
      }
      return tool === 'Bash' && /(^|[|&;\s])vale(\s|$)/.test(input.command || '');
    },
    context:
      'This touches a Vale style gate. Invoke the `docs-linter` skill: it carries the ' +
      'config traps, the ready rule files, and the requirement that a new rule ships ' +
      'with a probe it catches and a probe it does not over-catch.',
  },
  {
    id: 'commit',
    applies: (tool, input) =>
      tool === 'Bash' && /(^|[|&;\s])git\s+commit(\s|$)/.test(input.command || ''),
    context:
      'A commit is being prepared. Invoke the `commit-messages` skill and write the ' +
      'message to Conventional Commits 1.0.0 as that skill specifies.',
  },
];

// One reminder per category per session. A repeat costs tokens and says nothing new.
function alreadySent(sessionId, ruleId) {
  const session = String(sessionId || 'no-session').replace(/[^\w-]/g, '') || 'no-session';
  const dir = path.join(os.tmpdir(), 'claude-skill-reminder');
  const stamp = path.join(dir, `${session}.${ruleId}`);
  try {
    fs.mkdirSync(dir, { recursive: true });
    if (fs.existsSync(stamp)) return true;
    fs.writeFileSync(stamp, '');
  } catch {
    // An unwritable stamp costs a duplicate reminder, which is not worth failing over.
  }
  return false;
}

function main() {
  let payload;
  try {
    payload = JSON.parse(fs.readFileSync(0, 'utf8'));
  } catch {
    return;
  }

  const tool = payload.tool_name || '';
  const input = payload.tool_input || {};
  const rule = RULES.find((r) => r.applies(tool, input));
  if (!rule) return;
  if (alreadySent(payload.session_id, rule.id)) return;

  process.stdout.write(
    JSON.stringify({
      suppressOutput: true,
      hookSpecificOutput: {
        hookEventName: 'PreToolUse',
        additionalContext: rule.context,
      },
    })
  );
}

main();
