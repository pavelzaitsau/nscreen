// Conventional Commits 1.0.0, tuned to the commit-messages skill.
//   npm i -D @commitlint/cli @commitlint/config-conventional
module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'type-enum': [
      2,
      'always',
      ['feat', 'fix', 'perf', 'refactor', 'test', 'docs', 'build', 'ci', 'chore', 'revert'],
    ],
    'type-case': [2, 'always', 'lower-case'],
    'scope-case': [2, 'always', 'lower-case'],
    'subject-empty': [2, 'never'],
    'subject-full-stop': [2, 'never', '.'],
    'header-max-length': [2, 'always', 72],
    'header-min-length': [2, 'always', 15],

    // Off on purpose: a ticket id is uppercase and would fail every case rule.
    // The commit-msg hook checks the word after the id instead.
    'subject-case': [0],

    // The body is never wrapped: one paragraph per line, however long.
    'body-max-line-length': [0],
    'footer-max-line-length': [0],

    'body-leading-blank': [2, 'always'],
    'footer-leading-blank': [2, 'always'],
  },
};
