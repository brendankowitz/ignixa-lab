import { PATIENT_EXAMPLE } from '../fhirpath/sampleResources';

export const DEFAULT_VIEW_DEFINITION_TEXT = JSON.stringify(
  {
    resource: 'Patient',
    status: 'active',
    name: 'patient_demographics',
    select: [
      {
        column: [
          { name: 'id', path: 'id' },
          { name: 'gender', path: 'gender' },
          { name: 'birth_date', path: 'birthDate' },
        ],
      },
      {
        forEachOrNull: "name.where(use = 'official')",
        column: [
          { name: 'family', path: 'family' },
          { name: 'given', path: "given.join(' ')" },
        ],
      },
    ],
  },
  null,
  2,
);

const AMY: Record<string, unknown> = {
  resourceType: 'Patient',
  id: 'pt-amy',
  active: true,
  gender: 'female',
  birthDate: '1987-02-20',
  name: [{ use: 'official', family: 'Shaw', given: ['Amy', 'V.'] }],
  telecom: [{ system: 'email', value: 'amy.shaw@example.org' }],
};

const RI: Record<string, unknown> = {
  resourceType: 'Patient',
  id: 'pt-anon',
  active: false,
  gender: 'other',
  birthDate: '1990-07-31',
  name: [{ use: 'usual', given: ['Ri'] }],
};

export const DEFAULT_RESOURCES_TEXT = JSON.stringify([PATIENT_EXAMPLE, AMY, RI], null, 2);
