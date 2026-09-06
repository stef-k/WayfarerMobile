# Code Guard legacy adoption

The maintainer authorized a LOC ratchet during #270 implementation on 2026-09-06.
The baseline was generated with `code-guard . --create-loc-baseline` in a clean
source archive of reviewed main `7f0ddcbf0ad733b2c90f4a62a196b9a87a7ae7fb`.
It records 38 files above the default 600 counted-line failure threshold.
No #270 additions were used to establish allowances. The normal update command
then lowered MainViewModel's allowance from 1,189 to 1,186 lines.

Run `code-guard . --changed-only --json --json-mode compact` during work and
`code-guard . --base-ref <verified-base> --json --json-mode compact` after
committing to cover the complete branch. The runner discovers this baseline
automatically. Existing large files within their allowance report grandfathered
REVIEW; growth fails. This does not suppress structural or Markdown findings,
change thresholds, or exempt future code. Inspect REVIEW findings normally.

After a legitimate reduction, `code-guard . --update-loc-baseline` only lowers or
prunes allowances. Do not regenerate the baseline on a feature branch to admit
new debt, manually increase allowances, or change policy merely to pass a check.
