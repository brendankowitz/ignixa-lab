# Ignixa TestScript Engine Gaps Design

## Purpose

The FHIR suite reliability work in `ignixa-lab` exposed several gaps in
`Ignixa.TestScript`. The lab now contains reliable guards around those gaps,
but some of those guards are host-side preprocessors or postprocessors rather
than capabilities of the TestScript engine.

This design defines an umbrella issue for `brendankowitz/ignixa-fhir`. The
issue will inventory only gaps directly evidenced by the reliability work and
split implementation into focused follow-ups.

## Scope

The upstream issue covers four gaps:

1. Recognized Ignixa shorthand fields can be silently ignored by the parser.
2. Standard assertions cannot form a strict OR group.
3. Assertions cannot be conditionally applicable based on an earlier response.
4. Variable interpolation is inconsistent across operations, assertions, and
   inline fixture resources.

Lab package-cache behavior, target-server deviations, and inbound callback
hosting are outside this issue.

## Current Workarounds and Failure Modes

### Host-side content normalization

`TestScriptContentNormalizer` rewrites a test-level `requiresCapability`
shorthand property into the canonical
`http://ignixa.io/testscript/requiresCapability` extension before parsing.
Without it, the permissive parser ignores the recognized shorthand and the
capability gate is inert. The reliability work found 221 such fields.

Normalization belongs at the upstream parser boundary. Hosts should not need
to know which Ignixa authoring conveniences require rewriting.

### Warning-only alternatives plus post-execution enforcement

FHIR TestScript exposes a single criterion per assertion. Suites that need
`400 OR 422`, `404 OR 410`, or `200 OR 202 OR 204` currently use adjacent
`warningOnly` assertions. On their own, those assertions fail open because an
unexpected status merely produces warnings.

The lab compensates with:

- `StatusAlternativeEnforcementPlan`;
- `WarningOnlyStatusAlternativeEnforcer`;
- test-level `statusAlternativePolicy` markers;
- exact test-name mapping;
- method and action-order correlation; and
- description-text recognition for legacy delete alternatives.

The resulting final pass/fail status is strict, but the TestScript definition
and report are misleading. Valid alternatives produce warning noise, and
enforcement authority is split between the evaluator and a host postprocessor.

### Run-scoped definition rewriting

The evaluator resolves variables in some operation fields, including request
headers, but not consistently in assertion expected values or inline fixture
resource strings. `RunScopedDefinitionPreparer` therefore clones parsed
definitions, overwrites the `runId` default, and recursively rewrites fixture
JSON before each run.

This is necessary for run isolation today, but callers should be able to pass
initial variables while the evaluator resolves those variables consistently
without mutating parsed definitions.

## Proposed Architecture

### One upstream normalization boundary

`Ignixa.TestScript` will own normalization before typed parsing.

- Recognized shorthands normalize to canonical Ignixa FHIR extensions.
- Canonical and shorthand forms produce the same typed model and report.
- Supplying both is valid only when they are semantically identical.
- Conflicting or malformed recognized forms are parse errors.
- Unknown properties remain ignored to preserve permissive parsing.
- Normalization is exposed as a reusable public API and is automatically
  applied by the standard parser.

Initial recognized shorthands include `requiresCapability`. The assertion
features below are authored directly as valid FHIR extensions.

### Strict OR assertion groups

Assertions that represent alternatives remain ordinary standard TestScript
assertions. Each carries a group extension:

```json
{
  "assert": {
    "extension": [{
      "url": "http://ignixa.io/testscript/assertionAnyOfGroup",
      "valueString": "deleted-resource-readback"
    }],
    "description": "Preferred: 410 Gone for a tracked deleted resource",
    "response": "gone",
    "warningOnly": true
  }
},
{
  "assert": {
    "extension": [{
      "url": "http://ignixa.io/testscript/assertionAnyOfGroup",
      "valueString": "deleted-resource-readback"
    }],
    "description": "Alternative: 404 when deleted resources are not tracked",
    "response": "notFound",
    "warningOnly": true
  }
}
```

An aware engine treats assertions with the same group identifier in one test
as a single strict OR assertion:

- at least one applicable member must pass;
- if no applicable member passes, the group hard-fails;
- member `warningOnly` flags do not weaken the aggregate result;
- `warningOnly` remains on members so older engines can parse and execute the
  TestScript without rejecting one valid alternative; and
- a new engine reports one aggregate result, with member outcomes as diagnostic
  children rather than top-level warnings.

Compatibility with older engines is syntactic and non-breaking, not equivalent
enforcement. Older engines ignore the extension and remain fail-open for an
unexpected value because all members are warning-only.

Groups are scoped to one test. A group must contain at least two members with
the same response source and direction. An assertion may belong to only one
group. Nested or overlapping groups are invalid.

### Conditional assertion applicability

Some alternatives depend on an earlier operation. Classic Subscription delete
readback is the motivating case: an immediate GET may return `200` only when
the preceding DELETE returned `202`.

The conditional member carries a second extension:

```json
{
  "assert": {
    "sourceId": "readback-response",
    "extension": [
      {
        "url": "http://ignixa.io/testscript/assertionAnyOfGroup",
        "valueString": "subscription-delete-readback"
      },
      {
        "url": "http://ignixa.io/testscript/assertionWhenResponseStatus",
        "extension": [
          { "url": "sourceId", "valueString": "delete-response" },
          { "url": "status", "valueInteger": 202 }
        ]
      }
    ],
    "description": "An asynchronous delete may still be readable immediately",
    "responseCode": "200",
    "warningOnly": true
  }
}
```

The `404` and `410` members join the same OR group without the conditional
extension. The DELETE outcome is independently represented by a strict OR
group for `200`, `202`, and `204`.

Conditional semantics are:

- `sourceId` must identify an earlier operation response in the same test;
- one or more repeated `status` children define when the assertion applies;
- a nonmatching condition makes the member not applicable, not passed;
- a missing or duplicate response identifier is an execution error;
- forward or cross-test references are invalid; and
- an OR group with no applicable members is an execution error.

This generic composition replaces named policies such as
`subscription-delete-readback-v1` and `deleted-resource-readback-v1`.

### Consistent variable resolution

The evaluator will accept caller-supplied initial variables. Caller values take
precedence over TestScript `defaultValue` values.

Variables are resolved immutably in:

- operation URLs, parameters, headers, and bodies;
- assertion expected values, header names and values, URLs, and supported
  expression fields; and
- inline fixture resource string values before fixture creation.

Undefined variables produce an execution error that identifies the variable
and field. Escaped placeholders remain literal. Parsed definitions and fixture
resources are never mutated, allowing concurrent executions of one definition.

The lab can then generate `runId` once per suite execution and pass it as an
initial variable instead of rewriting definitions.

## Parsing and Evaluation Errors

Recognized extensions fail explicitly when they contain:

- missing or blank group identifiers;
- fewer than two OR-group members;
- malformed conditional children;
- HTTP statuses outside 100 through 599;
- duplicate, forward, or cross-test response references;
- overlapping group membership;
- conflicting canonical and shorthand values; or
- no applicable member at evaluation time.

Unknown, unrelated TestScript properties remain ignored.

## Reporting

The engine report is the sole authority for pass, warning, fail, error, and
skip outcomes.

For an OR group, the report contains one aggregate assertion result:

- group identifier;
- pass or fail;
- actual response status or evaluated value;
- applicable and inapplicable members; and
- the matched member when the group passes.

Member-level `warningOnly` compatibility flags do not create warning noise in
an aware engine. A genuinely informational assertion that is not part of a
strict group retains normal `warningOnly` behavior.

## Follow-up Work

The umbrella issue will track four focused follow-ups:

1. Add upstream normalization for recognized Ignixa shorthands and remove
   `TestScriptContentNormalizer`.
2. Add `assertionAnyOfGroup` and `assertionWhenResponseStatus` parsing,
   evaluation, and reporting; migrate all current status policies; remove
   `StatusAlternativeEnforcementPlan` and
   `WarningOnlyStatusAlternativeEnforcer`.
3. Add caller initial variables and consistent assertion/fixture interpolation;
   remove `RunScopedDefinitionPreparer` and restore strict interpolated
   assertions.
4. Add parser, evaluator, reporting, compatibility, and concurrency fixtures
   upstream.

## Acceptance Criteria

- Valid alternatives produce one clean pass without warning noise in an aware
  engine.
- No matching OR member produces a hard failure despite compatibility
  `warningOnly` flags.
- Conditional members apply only for matching earlier response statuses.
- Older engines ignore the extensions without parse failure.
- Canonical and recognized shorthand inputs normalize to the same typed model.
- Variables resolve consistently in operations, assertions, and inline fixture
  resources.
- Concurrent runs do not mutate or contaminate shared definitions.
- `ignixa-lab` can delete all four workaround components and their specialized
  policy logic without losing behavior.
