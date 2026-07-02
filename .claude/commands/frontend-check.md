---
description: Lint and build the React frontend
---

Lint and build the Ignixa Lab frontend, then report the outcome.

Run these from the `frontend/` directory:

1. `npm install` (or `npm ci` in CI) — install dependencies.
2. `npm run lint` — lint with oxlint (must exit clean).
3. `npm run build` — type-check (`tsc -b`) and produce a production build.

If any step fails, summarize the file(s) and the root cause. Keep the frontend a
structural skeleton unless asked otherwise — do not add visual design. Report a
concise pass/fail summary at the end.
