# FHIRPath Resource Status Bar Design

**Date:** 2026-07-12
**Status:** Approved

## Goal

Add a status bar beneath the FHIRPath bench's editable test resource. Clicking
a JSON property key or value displays the exact FHIRPath to that element and
makes the path easy to copy.

## Interaction

- The status bar is attached to the bottom edge of the resource editor and
  remains visible while the JSON content scrolls.
- Before a selection, it reads `Click a JSON key or value`.
- Clicking either a property key or its value selects that source token and
  displays the same element path.
- Array positions are exact and zero-based, for example
  `name[0].given[1]`.
- Clicking whitespace or punctuation leaves the current path unchanged.
- Editing the resource or selecting another sample clears the current path so
  the bar cannot show a stale location.
- The copy action is an icon-only button with a tooltip and accessible label.
  On success, the icon briefly changes to a check mark.
- The displayed path remains selectable as text.

## Architecture

Keep the existing `HighlightedTextarea` rather than introducing a new editor
dependency. Add an optional callback that reports a completed pointer/caret
interaction and lets the parent inspect the textarea's source offset.

Add a focused JSON source-position resolver. It parses the current source into
property-key and value spans while retaining their character offsets. Given a
clicked offset, it returns the deepest matching span and:

- formats object properties as FHIRPath child access;
- formats array positions with `[index]`;
- uses escaped FHIRPath identifiers for property names that are not valid bare
  identifiers;
- returns no selection for whitespace or punctuation; and
- reports malformed JSON rather than guessing from partial text.

`FhirPathBench` owns the selected path and source span. When the resolver
returns a match, the bench updates the status bar and selects the matching key
or value range in the textarea to provide a subtle native selection highlight.

The status bar is a small FHIRPath-bench component rendered directly beneath
the `HighlightedTextarea` inside the test-resource card. It uses the existing
bench colors, monospace font, and clipboard conventions.

## Data Flow

1. The user completes a click in the resource textarea.
2. `HighlightedTextarea` reports the textarea and its current source offset.
3. The bench passes the current resource text and offset to the resolver.
4. A matching key or value span updates the selected path and textarea
   selection. Whitespace or punctuation does nothing.
5. The status bar renders the path and enables its copy icon.
6. The copy action writes the current path to the Clipboard API and reports
   success or failure in the bar.

## Error and Accessibility Behavior

Malformed JSON produces no guessed path. When the user attempts to inspect
malformed source, the status bar reads `Fix JSON to inspect a path`.

Clipboard rejection is surfaced as `Copy failed`; it is not treated as a
successful copy. The path remains visible and manually selectable.

The copy control is a native button with `aria-label="Copy FHIRPath"` and a
matching tooltip. Success and failure messages are exposed as status text. The
layout truncates long paths visually without removing their full text or
tooltip and remains usable at narrow viewport widths.

## Testing

The source-position resolver has automated coverage for:

- nested object keys and values;
- exact indexes in object and primitive arrays;
- a key and its value resolving to the same path;
- deepest-span selection;
- escaped property names;
- whitespace and punctuation;
- malformed and incomplete JSON; and
- offsets at token boundaries.

Component-level checks cover clearing a stale path after editing or changing
samples, copy success/failure feedback, and the empty/invalid states. The
frontend lint and production build remain clean, and the resource card is
visually checked at desktop and narrow widths.

## Out of Scope

- Replacing the resource editor with CodeMirror, Monaco, or another dependency.
- Automatically inserting the selected path into the expression field.
- Showing an index-free collection path alongside the exact path.
- Applying the inspector status bar to the validation or FML benches.
