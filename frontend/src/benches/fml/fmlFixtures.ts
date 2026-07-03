import { PATIENT_EXAMPLE } from '../fhirpath/sampleResources';

export const DEFAULT_MAP_TEXT = [
  'map "http://ignixa.dev/StructureMap/PatientToPerson" = "PatientToPerson"',
  '',
  'uses "http://hl7.org/fhir/StructureDefinition/Patient" alias Patient as source',
  'uses "http://hl7.org/fhir/StructureDefinition/Person" alias Person as target',
  '',
  'group PatientToPerson(source src : Patient, target tgt : Person) {',
  '  src.id as vId -> tgt.identifier = vId "copy_id";',
  '  src.gender as vG -> tgt.gender = vG "copy_gender";',
  '  src.birthDate as vB -> tgt.birthDate = vB "copy_birthDate";',
  '  src.active as vA -> tgt.active = vA "copy_active";',
  '  src.name.family as vF -> tgt.name.family = vF "map_family";',
  '  src.name.given as vGiv -> tgt.name.given = vGiv "map_given";',
  '  src.telecom.value as vT -> tgt.telecom.value = vT "map_telecom";',
  '  src.maritalStatus as vM -> tgt.maritalStatus = vM "copy_marital"; // absent in source',
  '}',
].join('\n');

export const DEFAULT_SOURCE_TEXT = JSON.stringify(PATIENT_EXAMPLE, null, 2);

export const DEFAULT_EXPECTED_TEXT = JSON.stringify(
  {
    resourceType: 'Person',
    identifier: 'example',
    gender: 'male',
    birthDate: '1974-12-25',
    active: true,
    name: { family: ['Chalmers', 'Windsor'], given: ['Peter', 'James', 'Jim', 'Peter', 'James'] },
    telecom: { value: ['(03) 5555 6473', 'p.chalmers@example.org'] },
  },
  null,
  2,
);
