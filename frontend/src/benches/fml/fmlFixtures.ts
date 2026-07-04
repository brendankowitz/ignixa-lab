import { PATIENT_EXAMPLE } from '../fhirpath/sampleResources';

// String literals must be single-quoted: Ignixa.FhirMappingLanguage's tokenizer treats
// double-quoted text as a DelimitedIdentifier, not a string literal (see backend
// FmlServiceTests.cs's ValidMap for the same convention).
export const DEFAULT_MAP_TEXT = [
  "map 'http://ignixa.dev/StructureMap/PatientToPerson' = 'PatientToPerson'",
  '',
  "uses 'http://hl7.org/fhir/StructureDefinition/Patient' alias Patient as source",
  "uses 'http://hl7.org/fhir/StructureDefinition/Person' alias Person as target",
  '',
  'group PatientToPerson(source src : Patient, target tgt : Person) {',
  "  src.id as vId -> tgt.identifier = vId 'copy_id';",
  "  src.gender as vG -> tgt.gender = vG 'copy_gender';",
  "  src.birthDate as vB -> tgt.birthDate = vB 'copy_birthDate';",
  "  src.active as vA -> tgt.active = vA 'copy_active';",
  "  src.name.family as vF -> tgt.name.family = vF 'map_family';",
  "  src.name.given as vGiv -> tgt.name.given = vGiv 'map_given';",
  "  src.telecom.value as vT -> tgt.telecom.value = vT 'map_telecom';",
  "  src.maritalStatus as vM -> tgt.maritalStatus = vM 'copy_marital'; // absent in source",
  '}',
].join('\n');

export const DEFAULT_SOURCE_TEXT = JSON.stringify(PATIENT_EXAMPLE, null, 2);

// Verified against a live run of the real backend (`StructureMap/$transform`), not hand-derived: for
// each repeating source (`name`, `telecom`), the map's flat one-line rules re-fire per source item and
// overwrite the single target `name`/`telecom` node each time, so only the LAST source value survives
// (`family`/`given`/`telecom.value` end up as scalars, not arrays) — except `identifier`, which the
// engine wraps in a single-element array because `Person.identifier` is itself a list-typed field.
export const DEFAULT_EXPECTED_TEXT = JSON.stringify(
  {
    resourceType: 'Person',
    identifier: ['example'],
    gender: 'male',
    birthDate: '1974-12-25',
    active: true,
    name: { family: 'Windsor', given: 'James' },
    telecom: { value: 'p.chalmers@example.org' },
  },
  null,
  2,
);
