# FHIR Suite Reliability Design

## Goal

Make the existing bundled FHIR TestScript suites reliable enough that failures from live target runs represent either target-server deviations or intentionally scoped optional behavior, not avoidable test noise.

## Context

The suite vetting pass ran all 87 current suites against:

- `https://subscriptions.argo.run/fhir/r4` (the actual FHIR R4 base advertised by the Argo landing page).
- `https://bkowitz-testdeploy.azurewebsites.net`.

The runs found useful conformance signals, but also several false-signal categories:

- Test/runner mechanics issues, such as literal `${obsVersionId}` and `${basicVersionId}` values appearing in header expectations.
- Fixtures that are invalid before the server behavior under test is reached.
- Tests exercising optional or unadvertised capabilities, such as PATCH, history, conditional delete, `_tag`, `_has`, CORS header shape, and server-specific operations.
- Assertions that are stricter than FHIR permits, such as expecting a Bundle entry status to equal only `201` when the server may include the reason phrase.
- Classic R4 Subscription tests running against a Backport/topic-oriented server without enough scoping to distinguish classic lifecycle support from Backport support.

## Design Decisions

### 1. Conservative reliability pass

This pass will fix the proven reliability issues from the two live target runs. It will not add a new Subscriptions Backport suite. The existing architecture stays intact:

- Bundled JSON TestScripts remain the source of truth.
- The Functions backend continues to execute suites through `Ignixa.TestScript`.
- Existing report and suite APIs stay unchanged.

The work is limited to:

- Test/runner correctness fixes.
- Fixture hygiene.
- Capability-aware gating.
- Assertion strictness cleanup where the current expectation is stricter than FHIR.
- Existing R4 classic Subscription suite hygiene.

### 2. Spec traceability for strict assertions

Every strict conformance assertion that is kept or introduced must be tied to the applicable FHIR specification language for the suite's FHIR version.

If behavior differs across FHIR versions, the suite must do one of the following:

- Gate the test with a `fhirVersion` or `requiresCapability` expression.
- Split the behavior into version-specific tests.
- Relax or skip the assertion where the specification does not make the behavior universal.

Assertions based on optional behavior, SHOULD-level behavior, implementation guidance, or server-specific extensions must not be treated as universal hard conformance failures unless the suite is explicitly scoped to that optional/server-specific behavior.

Examples that require spec confirmation during implementation:

- HTTP update, delete, vread, history, conditional update, conditional delete, and If-Match behavior.
- Bundle transaction atomicity and `Bundle.entry.response.status` shape.
- Search parameter error handling, AND/OR semantics, `_id`, `_tag`, `_has`, include, and modifier semantics.
- Content negotiation and `_format` handling.
- Classic R4 Subscription lifecycle versus Subscriptions Backport topic/filter semantics.
- CORS and custom headers, which are not universal FHIR conformance requirements.

### 3. Workstreams

#### Assertion and runner mechanics

Inspect how the engine captures variables and evaluates header/body assertions. If header assertions should support variable interpolation in the current engine surface, fix the integration or tests so expected values are resolved before comparison. If interpolation is not available through the package surface, adjust affected suites to avoid unsupported interpolation until it can be fixed upstream.

Also replace client-side-invalid test inputs, such as `Content-Type: Jibberish`, with syntactically valid but unsupported media types so the request reaches the server and tests actual FHIR HTTP behavior.

#### Fixture hygiene

Repair invalid setup resources and contaminated fixed-ID assumptions before judging server behavior:

- Use timezone-bearing `dateTime` values when a time component is present.
- Use valid R4 `MessageHeader.event[x]` values.
- Include required fields, such as `Observation.code`, in setup resources.
- Avoid assuming fixed IDs are new on a reused live target unless the suite first deletes or otherwise isolates them.
- Add setup assertions where missing so downstream failures do not obscure fixture creation failures.

#### Capability gates

Add `requiresCapability` gates at the suite or test level for capabilities that are not universally supported or were not advertised by the target in the vetting pass:

- PATCH and conditional PATCH.
- history and vread/versioning-dependent checks.
- conditional delete.
- `_tag`, `_has`, include/wildcard/iterate, custom SearchParameter behavior, and non-universal search modifiers.
- CRUD checks for resource types not advertised by the target.
- Microsoft/server-specific operations and headers.
- CORS checks where the suite is testing browser hosting behavior rather than base FHIR conformance.

Gated tests should report `skip` when the target does not advertise support, not `fail`.

#### Assertion strictness cleanup

Relax assertions only when the FHIR specification permits the observed variation. Examples:

- Accept `Bundle.entry.response.status` values that start with the expected three-digit code, such as `201 Created`.
- Treat 404 after delete as acceptable where the relevant FHIR version permits servers that do not track deleted resources to return 404 instead of 410.
- Use broad 4xx assertions only when the spec requires client-error rejection but does not require a single exact status.

Do not relax strict assertions that are backed by SHALL-level behavior for the applicable FHIR version.

#### Existing Subscription suite

Keep `Subscriptions/basic.json` as an R4/R4B classic, criteria-based Subscription lifecycle test. Do not add Backport coverage in this pass.

Improve it by:

- Making setup success explicit.
- Avoiding cascade failures when create/update fails.
- Handling delete readback consistently with the spec-supported 404/410 distinction.
- Tightening capability gating so the suite is scoped to classic Subscription lifecycle behavior rather than assuming every server with the Subscription resource supports the classic criteria/channel flow.

Argo's Backport/topic-based behavior should be treated as a reason to add a future Backport suite, not as a reason to weaken the classic R4 suite.

## Validation Strategy

Validation will combine automated local checks with targeted live-target reruns.

Local validation:

- Pack suites before restore/build/test.
- Run the backend build and xUnit tests.
- Ensure every bundled TestScript JSON parses and loads through the suite catalog.
- Add or update automated tests for runner or mapper behavior when the repository has a suitable test surface.

Live-target validation:

- Run the current local Functions backend.
- Rerun the touched suites against `https://subscriptions.argo.run/fhir/r4` and `https://bkowitz-testdeploy.azurewebsites.net`.
- Confirm known false-signal categories no longer appear in raw reports.
- Confirm capability-dependent behavior reports as `skip` rather than `fail` when the target does not advertise support.
- Confirm remaining failures are explainable as target deviations, optional/server-specific behavior, or intentionally strict conformance checks with a spec citation.

## Non-Goals

- Adding a new Subscriptions Backport suite.
- Reworking the suite/report API shape.
- Changing the frontend.
- Weakening strict conformance assertions that are backed by FHIR version-specific SHALL-level behavior.
- Fixing unrelated server deviations discovered during live runs.

## Success Criteria

- Existing backend build and tests pass.
- Bundled suites parse and load.
- The known runner/test bugs from the vetting pass are removed.
- Optional/unadvertised behavior is gated rather than reported as universal failure.
- Strict conformance failures have version-appropriate FHIR spec backing.
- The live-target rerun of touched suites produces a cleaner report where failures are actionable.
