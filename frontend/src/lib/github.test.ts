/// <reference types="node" />

import test from 'node:test';
import assert from 'node:assert/strict';
import { testScriptGithubUrl } from './github.ts';

test('testScriptGithubUrl links packaged suites to their upstream source revision', () => {
  assert.equal(
    testScriptGithubUrl('Bundles/batch.json', 'ce533c1cde74c8a22bb72bf2ecb429e60dca126a'),
    'https://github.com/brendankowitz/ignixa-fhir/blob/ce533c1cde74c8a22bb72bf2ecb429e60dca126a/src/Core/Ignixa.TestScript.Suites/testscripts/Bundles/batch.json',
  );
});

test('testScriptGithubUrl falls back to the upstream main branch when no revision is available', () => {
  assert.equal(
    testScriptGithubUrl('Bundles/batch.json', undefined),
    'https://github.com/brendankowitz/ignixa-fhir/blob/main/src/Core/Ignixa.TestScript.Suites/testscripts/Bundles/batch.json',
  );
});
