# Responsive UI repair design

## Goal

Fix medium, small, and mobile rendering issues across the whole frontend: the Conformance app and the Benches app. The repaired UI should avoid page-level horizontal overflow, keep primary navigation and actions reachable, preserve readable card/list content, and transition predictably at each existing breakpoint.

## Scope

- Conformance shell: sticky top bar, app switcher, Setup, Runner, and Report screens.
- Benches shell: sticky top bar, bench tabs, shared bench layout primitives, and the FHIRPath, Validation, Fakes, SQL on FHIR, and FML bench surfaces that use those primitives.
- Verification with Playwright across desktop, medium, small, and mobile viewport widths.

Out of scope: visual redesign, new navigation concepts, changing run/report data behavior, and large extraction of shared React shell components.

## Current findings

The Conformance app relies mostly on `frontend/src/App.css`, with one narrow breakpoint at 640px. That breakpoint stacks the runner panes and guards a few failure-detail layouts, but several medium and mobile surfaces still depend on desktop row layouts: the top bar, Setup suite rows, Runner status strip, test group headers, Report header, suite bars, and the coverage grid.

The Benches app uses inline styles plus `useIsNarrowViewport`. It already has useful patterns: compact page padding at 560px, header compaction at 680px, and editor/result pane stacking around 720px or 840px. The main risks are top bar height/overflow, wrapped control groups, nested pill selectors, fixed-width command/input rows, and long code or JSON content.

## Recommended approach

Use a systematic responsive pass rather than either a minimal overflow patch or a broad shell refactor.

1. Add targeted breakpoint behavior to the Conformance CSS for medium, small, and mobile widths.
2. Tighten Benches inline layout primitives and per-bench control groups using the existing `compact` and `stacked` flags.
3. Add Playwright checks that exercise the most fragile viewports and assert that the document does not overflow horizontally.

This approach fixes the actual rendering failures while keeping the implementation localized to existing layout surfaces.

## Responsive behavior

### Shared expectations

- Desktop remains visually unchanged unless a change is needed to support shrink-safe layout, such as `min-width: 0`.
- Medium widths should wrap toolbars and headers intentionally instead of relying on text truncation or accidental flex wrapping.
- Small/mobile widths should prefer stacked cards, full-width primary controls, and horizontally scrollable dense data grids where a true one-column rendering would damage readability.
- Long technical strings, JSON, URLs, commands, and suite/test identifiers should wrap or truncate inside their own containers, not expand the page.

### Conformance app

- Top bar: allow the app switcher and screen tabs to occupy their own rows at narrow widths; keep share/theme/run controls visible; hide the server readout on small screens as it does today.
- Setup screen: reduce padding on mobile, let the endpoint prefix/input/run button wrap or shrink safely, and convert dense suite rows into readable two-line rows when space is constrained.
- Runner screen: keep the two-column suite tree/test list on desktop and medium screens, stack at mobile widths, and make the status strip wrap without pushing content off-screen.
- Test list: wrap the toolbar, group headers, and row headers; keep status chips and important actions reachable; preserve existing detail tabs and HTTP body wrapping.
- Report screen: make the report header, suite bars, and action buttons stack cleanly; keep the coverage grid horizontally scrollable inside the panel rather than letting it widen the page.

### Benches app

- Reuse existing breakpoints: 560px for compact controls, 680px for header compaction, 720px for most two-column bench panes, and 840px for Validation.
- Update shared bench primitives so pill groups, headers, cards, and button rows shrink safely.
- Update per-bench control rows that currently use nowrap or fixed widths so they can wrap or stack on compact screens.
- Preserve the current desktop structure and inline-style pattern; do not introduce a CSS framework or route-level redesign.

## Error handling and accessibility

No data or API behavior changes are expected. Existing error banners, disabled controls, and loading states remain intact. Responsive changes should preserve semantic elements, button labels, aria labels, and keyboard-reachable controls.

## Verification

Use Playwright to render both apps at representative viewport widths: desktop around 1280px, medium around 840px, small around 680px, and mobile around 400px. At each viewport:

- load the Conformance and Benches entry points,
- exercise the primary tabs or bench tabs,
- assert `document.documentElement.scrollWidth <= document.documentElement.clientWidth`,
- check that sticky headers and primary actions remain visible,
- inspect key stacked layouts such as Runner panes, Report coverage, and bench editor/result panes.

Also run the existing frontend lint/build checks after implementation.

## Implementation boundaries

Prefer CSS and existing inline-style helpers over structural rewrites. Only touch component markup where CSS cannot express the required layout safely. Keep changes localized to frontend responsive layout files and any Playwright verification files needed for the new checks.
