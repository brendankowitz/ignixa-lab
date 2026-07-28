/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { buildUrl } from './searchApi.ts';
import type { SearchRequest } from './searchTypes.ts';

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
