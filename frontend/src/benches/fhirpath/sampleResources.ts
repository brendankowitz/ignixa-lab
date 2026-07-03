export type SampleId = 'patient' | 'observation';

export interface FhirResourceFixture {
  id: SampleId;
  label: string;
  data: Record<string, unknown>;
}

export const PATIENT_EXAMPLE: Record<string, unknown> = {
  resourceType: 'Patient',
  id: 'example',
  active: true,
  name: [
    { use: 'official', family: 'Chalmers', given: ['Peter', 'James'] },
    { use: 'usual', given: ['Jim'] },
    { use: 'maiden', family: 'Windsor', given: ['Peter', 'James'], period: { end: '2002' } },
  ],
  telecom: [
    { system: 'phone', value: '(03) 5555 6473', use: 'work', rank: 1 },
    { system: 'email', value: 'p.chalmers@example.org', use: 'home' },
  ],
  gender: 'male',
  birthDate: '1974-12-25',
  address: [{ use: 'home', line: ['534 Erewhon St'], city: 'PleasantVille', state: 'Vic', postalCode: '3999' }],
};

export const OBSERVATION_EXAMPLE: Record<string, unknown> = {
  resourceType: 'Observation',
  id: 'blood-pressure',
  status: 'final',
  category: [
    {
      coding: [
        { system: 'http://terminology.hl7.org/CodeSystem/observation-category', code: 'vital-signs', display: 'Vital Signs' },
      ],
    },
  ],
  code: {
    coding: [{ system: 'http://loinc.org', code: '85354-9', display: 'Blood pressure panel' }],
    text: 'Blood pressure',
  },
  subject: { reference: 'Patient/example' },
  effectiveDateTime: '2026-05-02T09:30:00Z',
  component: [
    {
      code: { coding: [{ system: 'http://loinc.org', code: '8480-6', display: 'Systolic blood pressure' }] },
      valueQuantity: { value: 127, unit: 'mmHg', system: 'http://unitsofmeasure.org', code: 'mm[Hg]' },
    },
    {
      code: { coding: [{ system: 'http://loinc.org', code: '8462-4', display: 'Diastolic blood pressure' }] },
      valueQuantity: { value: 81, unit: 'mmHg', system: 'http://unitsofmeasure.org', code: 'mm[Hg]' },
    },
  ],
};

export const SAMPLE_RESOURCES: FhirResourceFixture[] = [
  { id: 'patient', label: 'Patient', data: PATIENT_EXAMPLE },
  { id: 'observation', label: 'Observation', data: OBSERVATION_EXAMPLE },
];

export const EXAMPLE_EXPRESSIONS: Record<SampleId, string[]> = {
  patient: [
    "name.where(use = 'official').given.first()",
    "telecom.where(system = 'phone').value",
    'name.given.count()',
    "name.select(given.first() & ' ' & family)",
  ],
  observation: [
    "component.where(code.coding.code = '8480-6').value.value",
    "code.coding.display.join(' / ')",
    'component.count()',
    'effective',
  ],
};

export const DEFAULT_EXPRESSION = EXAMPLE_EXPRESSIONS.patient[0];
