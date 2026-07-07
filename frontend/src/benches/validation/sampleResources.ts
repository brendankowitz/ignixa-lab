export type ValidationSampleId = 'patient-valid' | 'patient-invalid' | 'bundle-invalid' | 'custom';

export const VALIDATION_SAMPLES: {
  id: Exclude<ValidationSampleId, 'custom'>;
  label: string;
  data: Record<string, unknown>;
}[] = [
  {
    id: 'patient-valid',
    label: 'Valid Patient',
    data: {
      resourceType: 'Patient',
      id: 'example',
      active: true,
      name: [{ family: 'Chalmers', given: ['Peter'] }],
      gender: 'male',
      birthDate: '1974-12-25',
    },
  },
  {
    id: 'patient-invalid',
    label: 'Invalid Patient',
    data: {
      resourceType: 'Patient',
      active: 'not-a-boolean',
      gender: 'definitely',
      birthDate: '25/12/1974',
    },
  },
  {
    id: 'bundle-invalid',
    label: 'Bundle',
    data: {
      resourceType: 'Bundle',
      type: 'transaction',
      entry: [
        {
          resource: { resourceType: 'Patient', id: 'p1' },
        },
      ],
    },
  },
];

export const DEFAULT_VALIDATION_RESOURCE = JSON.stringify(VALIDATION_SAMPLES[1].data, null, 2);
