import { describe, it } from 'node:test';
import assert from 'node:assert/strict';
import {
  compartmentMemberOptions,
  everythingTypeFilterOptions,
  isCompartmentRoot,
  RESOURCE_TYPES,
  searchModesFor,
  type ResourceType,
} from './searchTypes.ts';

describe('searchModesFor', () => {
  it('offers $everything for Patient only', () => {
    // The backend exposes a Patient-anchored $everything route and no other, so offering the mode elsewhere
    // would produce a request with no route behind it.
    assert.deepEqual(
      RESOURCE_TYPES.filter((type) => searchModesFor(type).includes('everything')),
      ['Patient'],
    );
  });

  it('offers Compartment mode for exactly the compartment roots', () => {
    assert.deepEqual(
      RESOURCE_TYPES.filter((type) => searchModesFor(type).includes('compartment')),
      RESOURCE_TYPES.filter(isCompartmentRoot),
    );
  });

  it('offers type search for every resource type', () => {
    // handleResourceTypeChange falls back to 'type' whenever the current mode is unavailable on the new
    // resource type, so a type without it would strand the bench in an unavailable mode.
    for (const type of RESOURCE_TYPES) {
      assert.ok(searchModesFor(type).includes('type'), `${type} must offer type search`);
    }
  });
});

describe('compartmentMemberOptions', () => {
  it('offers the wildcard for every root', () => {
    for (const root of RESOURCE_TYPES.filter(isCompartmentRoot)) {
      assert.ok(compartmentMemberOptions(root).includes('*'), `${root} must offer the wildcard`);
    }
  });

  it('includes Patient in its own compartment', () => {
    // A Patient is a member of the Patient compartment (via `link`); excluding the root would withhold a
    // legal option. Pinned on the backend by CompartmentMembershipContractTests.
    assert.ok(compartmentMemberOptions('Patient').includes('Patient'));
  });

  it('never offers Patient under the Encounter root', () => {
    // The asymmetry that made this a bug: Patient is not an Encounter compartment member in any supported
    // FHIR version, so this pill would always 400.
    assert.ok(!compartmentMemberOptions('Encounter').includes('Patient'));
  });

  it('offers Observation and Encounter under both roots', () => {
    for (const root of ['Patient', 'Encounter'] as const) {
      const options = compartmentMemberOptions(root);
      assert.ok(options.includes('Observation'), `${root} must offer Observation`);
      assert.ok(options.includes('Encounter'), `${root} must offer Encounter`);
    }
  });

  it('offers only known resource types plus the wildcard', () => {
    for (const root of RESOURCE_TYPES.filter(isCompartmentRoot)) {
      for (const option of compartmentMemberOptions(root)) {
        assert.ok(
          option === '*' || (RESOURCE_TYPES as readonly string[]).includes(option),
          `${root} offered an unknown member type: ${option}`,
        );
      }
    }
  });
});

describe('isCompartmentRoot', () => {
  it('accepts Patient and Encounter and rejects Observation', () => {
    assert.equal(isCompartmentRoot('Patient'), true);
    assert.equal(isCompartmentRoot('Encounter'), true);
    assert.equal(isCompartmentRoot('Observation'), false);
  });
});

describe('everythingTypeFilterOptions', () => {
  it('omits Patient, the operation anchor', () => {
    assert.ok(!everythingTypeFilterOptions().includes('Patient'));
  });

  it('offers only Patient-compartment members, which the backend requires', () => {
    // The backend 400s a _type value that is not a Patient compartment member, so every option here must be
    // one. Patient's compartment membership is the authority.
    const patientMembers = compartmentMemberOptions('Patient');
    for (const type of everythingTypeFilterOptions()) {
      assert.ok(patientMembers.includes(type), `_type option ${type} is not a Patient compartment member`);
    }
  });

  it('offers every non-Patient resource type', () => {
    assert.deepEqual(
      [...everythingTypeFilterOptions()],
      RESOURCE_TYPES.filter((type: ResourceType) => type !== 'Patient'),
    );
  });
});
