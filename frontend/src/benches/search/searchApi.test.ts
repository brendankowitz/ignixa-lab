/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { buildUrl, runSearch } from './searchApi.ts';
import type { SearchRequest } from './searchTypes.ts';

const TYPE_REQUEST: SearchRequest = { mode: 'type', fhirVersion: 'R4', resourceType: 'Patient', query: 'name=Smith' };

/** Runs `runSearch` against a stubbed `fetch` returning `body` with `status`, restoring the global after. */
async function runWithStubbedFetch(body: string, status = 200, statusText = 'OK'): Promise<unknown> {
  const realFetch = globalThis.fetch;
  globalThis.fetch = (() =>
    Promise.resolve({
      ok: status >= 200 && status < 300,
      status,
      statusText,
      text: () => Promise.resolve(body),
    })) as unknown as typeof globalThis.fetch;
  try {
    return await runSearch(TYPE_REQUEST, new AbortController().signal);
  } finally {
    globalThis.fetch = realFetch;
  }
}

test('runSearch returns the parsed trace for a well-formed 200', async () => {
  const result = await runWithStubbedFetch(
    JSON.stringify({ fhirVersion: 'R4', resourceType: 'Patient', parameters: [], implicit: [] }),
  );

  assert.equal((result as { resourceType: string }).resourceType, 'Patient');
});

test('runSearch surfaces the backend\'s { error } body on a non-2xx', async () => {
  await assert.rejects(
    runWithStubbedFetch(JSON.stringify({ error: "'Nope' is not a supported FHIR resource type for R4." }), 400, 'Bad Request'),
    /is not a supported FHIR resource type/,
  );
});

test('runSearch quotes the body when a 200 is not JSON, rather than reporting "failed with status 200"', async () => {
  // The misconfigured-VITE_API_BASE_URL case: /api/... falls through to the SPA's index.html, which answers
  // 200 with HTML. Reporting only the status produces a self-contradicting, unactionable message.
  await assert.rejects(
    runWithStubbedFetch('<!doctype html><title>app</title>'),
    (error: Error) => {
      assert.match(error.message, /non-JSON response \(HTTP 200\)/);
      assert.match(error.message, /VITE_API_BASE_URL/);
      assert.match(error.message, /<!doctype html>/);
      return true;
    },
  );
});

test('runSearch quotes the body when a non-2xx is not JSON', async () => {
  await assert.rejects(
    runWithStubbedFetch('<html>502 Bad Gateway</html>', 502, 'Bad Gateway'),
    /502 Bad Gateway.*502 Bad Gateway/s,
  );
});

test('runSearch rejects a 200 whose body is not a search trace', async () => {
  // Without the shape check this is cast blind and throws inside React's render phase instead, which
  // unmounts the bench to a blank page rather than showing the error banner.
  await assert.rejects(runWithStubbedFetch(JSON.stringify({ unexpected: true })), /Expected a search trace/);
  await assert.rejects(runWithStubbedFetch('null'), /Expected a search trace/);
});

test('buildUrl (type mode) builds /api/search/{fhirVersion}/{resourceType}?{query}', () => {
  const request: SearchRequest = { mode: 'type', fhirVersion: 'R4', resourceType: 'Patient', query: 'name=Smith' };

  assert.equal(buildUrl(request), '/api/search/R4/Patient?name=Smith');
});

test('buildUrl (type mode) omits the "?" suffix entirely for a blank/whitespace-only query', () => {
  assert.equal(
    buildUrl({ mode: 'type', fhirVersion: 'R4', resourceType: 'Patient', query: '' }),
    '/api/search/R4/Patient',
  );
  assert.equal(
    buildUrl({ mode: 'type', fhirVersion: 'R4', resourceType: 'Patient', query: '   ' }),
    '/api/search/R4/Patient',
  );
});

test('buildUrl (compartment mode) builds /api/search/{fhirVersion}/{compartmentType}/{compartmentId}/{memberType}?{query}', () => {
  const request: SearchRequest = {
    mode: 'compartment',
    fhirVersion: 'R4',
    compartmentType: 'Patient',
    compartmentId: 'example',
    memberType: 'Observation',
    query: 'code=1234-5',
  };

  assert.equal(buildUrl(request), '/api/search/R4/Patient/example/Observation?code=1234-5');
});

test('buildUrl (compartment mode) passes the "*" wildcard member type through unencoded (encodeURIComponent leaves "*" alone)', () => {
  const request: SearchRequest = {
    mode: 'compartment',
    fhirVersion: 'R4',
    compartmentType: 'Encounter',
    compartmentId: 'example',
    memberType: '*',
    query: '',
  };

  assert.equal(buildUrl(request), '/api/search/R4/Encounter/example/*');
});

test('buildUrl (everything mode) builds the bare $everything URL with no query string when every optional field is unset', () => {
  const request: SearchRequest = {
    mode: 'everything',
    fhirVersion: 'R4',
    patientId: 'example',
    typeFilter: [],
    since: '',
    start: '',
    end: '',
    includeReferencedResources: true,
  };

  assert.equal(buildUrl(request), '/api/search/R4/Patient/example/$everything');
});

test('buildUrl (everything mode) does NOT percent-encode the literal "$everything" segment', () => {
  const url = buildUrl({
    mode: 'everything',
    fhirVersion: 'R4',
    patientId: 'example',
    typeFilter: [],
    since: '',
    start: '',
    end: '',
    includeReferencedResources: true,
  });

  assert.ok(url.endsWith('/$everything'), `expected the URL to end with the literal "/$everything" segment, got: ${url}`);
  assert.ok(!url.includes('%24'), 'the "$" must not be percent-encoded (encodeURIComponent is deliberately not applied to this segment)');
});

test('buildUrl (everything mode) sets _type/_since/start/end/includeReferencedResources as query params only when provided', () => {
  const request: SearchRequest = {
    mode: 'everything',
    fhirVersion: 'R4',
    patientId: 'example',
    typeFilter: ['Observation', 'Encounter'],
    since: '2026-01-01T00:00:00Z',
    start: '2020-01-01T00:00:00Z',
    end: '2026-01-01T00:00:00Z',
    includeReferencedResources: false,
  };

  const url = buildUrl(request);
  const query = new URLSearchParams(url.split('?')[1]);
  assert.equal(query.get('_type'), 'Observation,Encounter');
  assert.equal(query.get('_since'), '2026-01-01T00:00:00Z');
  assert.equal(query.get('start'), '2020-01-01T00:00:00Z');
  assert.equal(query.get('end'), '2026-01-01T00:00:00Z');
  assert.equal(query.get('includeReferencedResources'), 'false');
});

test('buildUrl (everything mode) omits includeReferencedResources entirely when true (the default)', () => {
  const url = buildUrl({
    mode: 'everything',
    fhirVersion: 'R4',
    patientId: 'example',
    typeFilter: [],
    since: '',
    start: '',
    end: '',
    includeReferencedResources: true,
  });

  assert.ok(!url.includes('includeReferencedResources'), `expected no includeReferencedResources param, got: ${url}`);
});

test('buildUrl applies encodeURIComponent to fhirVersion/resourceType, not just the free-text id fields', () => {
  // fhirVersion/resourceType are narrow string-union types (FhirVersion/ResourceType), so a real caller can
  // never actually hand buildUrl a value needing escaping there -- but the implementation calls
  // encodeURIComponent on them unconditionally, the same as the free-text id fields covered by the tests
  // below. Casting past the type system here to prove that at the runtime level, not just for the id fields
  // where a hostile value is actually reachable. "/" is the sharpest probe for "was this actually encoded":
  // an unencoded "/" would silently inject an extra path segment instead of a visibly-escaped %2F.
  const request = { mode: 'type', fhirVersion: 'R4/evil', resourceType: 'Patient/evil', query: '' } as unknown as SearchRequest;

  assert.equal(buildUrl(request), '/api/search/R4%2Fevil/Patient%2Fevil');
});

test('buildUrl (compartment mode) encodes compartmentId', () => {
  const request: SearchRequest = {
    mode: 'compartment',
    fhirVersion: 'R4',
    compartmentType: 'Patient',
    compartmentId: 'a/b',
    memberType: 'Observation',
    query: '',
  };

  assert.equal(buildUrl(request), '/api/search/R4/Patient/a%2Fb/Observation');
});

test('buildUrl (everything mode) encodes patientId', () => {
  const request: SearchRequest = {
    mode: 'everything',
    fhirVersion: 'R4',
    patientId: 'a/b',
    typeFilter: [],
    since: '',
    start: '',
    end: '',
    includeReferencedResources: true,
  };

  assert.equal(buildUrl(request), '/api/search/R4/Patient/a%2Fb/$everything');
});
