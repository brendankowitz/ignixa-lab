# Responsive landing feature demos design

## Goal

Repair the static landing page so it no longer feels desktop-only or overflows on smaller screens, while replacing the current FHIRPath-only hero card with a rotating demo surface that showcases the full Ignixa toolkit.

The landing page remains desktop-first visually. Smaller screens should stack and wrap predictably rather than receive a separate phone-first redesign.

## Scope

- Static root landing page at `frontend/index.html`.
- Landing-page CSS extracted from inline styles into reusable classes in the existing `frontend/index.html` style block.
- Hero demo content for FHIRPath, Fakes, Validation, Conformance, SQL-on-FHIR, and FML.
- Accessibility and motion behavior for the rotating demo controls.
- Verification through the existing frontend build and responsive check.

Out of scope: redesigning the Conformance app, redesigning the Benches app, changing FHIR execution behavior, adding new dependencies, or converting the landing page into a React route.

## Current findings

The root landing page is a static Vite entry point. It currently keeps nearly all landing styles inline in `frontend/index.html`. The hero uses a fixed two-column grid and a single FHIRPath code card. Navigation, CTA rows, stats, launcher cards, and the footer also rely on desktop-oriented inline flex/grid rules.

Recent responsive work covered the Conformance and Benches app surfaces, but the static landing page was not included in that pass. The existing `responsive:check` script is available for validating horizontal overflow after this landing-page work.

## Recommended approach

Replace the hero code card with one rotating multi-feature demo widget and make the landing page shrink-safe through targeted landing-page classes.

This is preferable to adding a lower carousel or static grid because it addresses both problems at once: the hero no longer implies Ignixa is only a FHIRPath tool, and the responsive repair can focus on one polished demo surface instead of adding more below-the-fold weight.

## Responsive behavior

Desktop remains a two-column hero with strong visual parity between copy and demo. As the viewport narrows:

- the top bar wraps without hiding Conformance, Benches, or GitHub links;
- hero copy stacks above the demo card before any horizontal overflow appears;
- headline and body copy use fluid sizing, such as `clamp()`, instead of fixed desktop-only sizes;
- CTA buttons wrap or become full-width only when needed;
- stats wrap as compact text chips rather than forcing a wide row;
- launcher cards move from two columns to one column;
- code, JSON, URLs, and long labels wrap inside their own containers instead of expanding the page;
- the footer wraps its metadata and GitHub link cleanly.

The target is a desktop-first page that remains usable and visually intentional on smaller screens, especially by preventing overflow rather than rebuilding the landing experience for phones.

## Rotating demo widget

The hero demo uses a shared shell with:

- a title or feature label;
- a compact code, query, resource, or report snippet;
- small chips that explain what the user is seeing;
- a result, status, or output line;
- a row of feature buttons for direct selection.

Initial demo slides:

| Feature | Example |
| --- | --- |
| FHIRPath | Evaluate `Patient.name.where(use='official').given.first()` and show `"Jane"`. |
| Fakes | Generate a synthetic patient or care scenario summary. |
| Validation | Validate a resource and show an issue-count/status result. |
| Conformance | Run selected TestScripts and show pass/fail summary. |
| SQL-on-FHIR | Query observations or encounters and show a tabular result. |
| FML | Transform source data into a target resource preview. |

The widget auto-rotates slowly on pointer-capable default-motion environments. Users can click feature buttons to choose a slide, and manual selection pauses automatic rotation for the rest of that page session so the selected content is readable.

## Accessibility and reduced motion

Feature selectors are real buttons with clear labels and `aria-pressed` state. The active slide is visible without relying on color alone. Auto-rotation is disabled when `prefers-reduced-motion: reduce` is set. The demo should not trap focus, and all important landing actions remain keyboard reachable.

## Data flow and implementation boundaries

The landing page remains static and dependency-free. Demo slides are local static content, not fetched from APIs or executed against the backend. A small inline script will manage slide state, timers, button state, and reduced-motion handling.

CSS should replace the fragile inline layout rules with named landing-page classes so the responsive behavior is centralized and easier to verify. Avoid broad app-shell refactors or shared React component extraction.

## Error handling

There are no network or runtime data errors to surface because the landing demos are illustrative static examples. JavaScript should enhance the page, not make it unusable: if script execution fails, the first demo remains visible and the primary navigation and CTAs still work.

## Verification

After implementation:

- run the frontend build;
- run the existing responsive check;
- confirm the landing page has no document-level horizontal overflow at representative desktop and smaller viewport widths;
- confirm slide controls work with keyboard and pointer input;
- confirm reduced-motion disables automatic rotation.
