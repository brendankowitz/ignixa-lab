/**
 * Curated group + blurb text for predefined clinical scenarios, keyed by the real
 * scenario id `Ignixa.FhirFakes`'s `ScenarioCatalog` reports (the `Get` prefix is
 * stripped by the discovery convention, so ids look like `DiabeticPatient`). Grouping
 * prefers the library's own `Category` over this curated map; the curated map remains
 * the source for `blurb` text, and the `Scenario` fallback still applies when neither
 * the library nor the curated map has a group (see `describeScenario`). This pattern
 * lets the bench keep working if the library adds scenarios later — same as `edgeCaseDescriptions.ts`.
 */
interface ScenarioDescription {
  group: string;
  blurb: string;
}

const SCENARIO_DESCRIPTIONS: Record<string, ScenarioDescription> = {
  // Emergency (EmergencyDepartmentScenario)
  ChestPainVisit: { group: 'Emergency', blurb: 'ED workup for acute chest pain — triage, ECG, troponin, and disposition.' },
  AbdominalPainVisit: { group: 'Emergency', blurb: 'ED evaluation of acute abdominal pain with labs and imaging.' },
  MinorTraumaVisit: { group: 'Emergency', blurb: 'ED visit for minor trauma — soft-tissue and laceration care.' },
  FractureVisit: { group: 'Emergency', blurb: 'ED presentation of a limb fracture through imaging and immobilization.' },

  // Preventive care (ComprehensivePreventiveCareScenario)
  PediatricWellChildVisit: { group: 'Preventive', blurb: 'Routine well-child check with growth tracking and immunizations.' },
  AdultAnnualPhysical: { group: 'Preventive', blurb: 'Adult annual physical with preventive screening and baseline vitals.' },
  SeniorMedicareWellnessVisit: { group: 'Preventive', blurb: 'Medicare annual wellness visit with geriatric screening.' },

  // Wellness (WellnessVisitScenario / EnhancedWellnessVisitScenario)
  WellnessVisit: { group: 'Wellness', blurb: 'General wellness visit with baseline vitals and screening labs.' },
  EnhancedWellnessVisit: { group: 'Wellness', blurb: 'Wellness visit with probabilistic screening findings and follow-up.' },
  ComprehensiveScreeningVisit: { group: 'Wellness', blurb: 'Broad preventive screening panel across multiple risk areas.' },

  // Infection (EarInfectionScenario / UrinaryTractInfectionScenario)
  PediatricEarInfection: { group: 'Infection', blurb: 'Pediatric acute otitis media diagnosis and antibiotic course.' },
  UrinaryTractInfection: { group: 'Infection', blurb: 'Uncomplicated UTI workup with urinalysis and treatment.' },

  // Chronic disease (ChronicDiseaseScenario / MetabolicSyndromeProgressionScenario)
  ChronicKidneyDiseaseProgression: { group: 'Chronic', blurb: 'Longitudinal CKD progression with declining eGFR over time.' },
  COPDManagementWithExacerbations: { group: 'Chronic', blurb: 'COPD maintenance therapy punctuated by acute exacerbations.' },
  MetabolicSyndromeProgression: { group: 'Chronic', blurb: 'Progression of metabolic syndrome across weight, lipids, and glucose.' },
  BMICorrelationDemo: { group: 'Chronic', blurb: 'Demonstration cohort correlating BMI with related observations.' },

  // Respiratory (AsthmaticChildScenario / PediatricAsthmaOnsetScenario)
  AsthmaticChild: { group: 'Respiratory', blurb: 'Pediatric asthma follow-up with peak-flow trends and a rescue inhaler.' },
  PediatricAsthmaOnset: { group: 'Respiratory', blurb: 'First-onset pediatric asthma diagnosis and controller therapy start.' },
  AllergicMarchAsthma: { group: 'Respiratory', blurb: 'Atopic march from early allergy to asthma across childhood.' },

  // Endocrine / cardiovascular (DiabeticPatient / HypertensivePatient / CardiovascularScenario)
  DiabeticPatient: { group: 'Cardiometabolic', blurb: 'Type 2 diabetes with medication escalation as A1c rises.' },
  HypertensivePatient: { group: 'Cardiometabolic', blurb: 'Hypertension management with stepwise medication escalation.' },
  AcuteMyocardialInfarction: { group: 'Cardiometabolic', blurb: 'Acute MI presentation through intervention and secondary prevention.' },
  CongestiveHeartFailureExacerbation: { group: 'Cardiometabolic', blurb: 'CHF exacerbation with decompensation, diuresis, and recovery.' },
  IschemicStrokeWithRehabilitation: { group: 'Cardiometabolic', blurb: 'Ischemic stroke through acute care and rehabilitation.' },

  // Cancer care (CancerCarePathwayScenario)
  BreastCancerPathway: { group: 'Cancer', blurb: 'Breast cancer pathway from screening through diagnosis and treatment.' },
  ColorectalCancerPathway: { group: 'Cancer', blurb: 'Colorectal cancer pathway from screening colonoscopy to therapy.' },
  LungCancerPathway: { group: 'Cancer', blurb: 'Lung cancer pathway from nodule detection through staging and treatment.' },
  ProstateCancerPathway: { group: 'Cancer', blurb: 'Prostate cancer pathway from PSA screening through management.' },
  ComprehensiveCancerScreening: { group: 'Cancer', blurb: 'Multi-site cancer screening battery for an average-risk adult.' },

  // Mental health (MentalHealthTreatmentScenario)
  DepressionScreeningAndTreatment: { group: 'Mental health', blurb: 'Depression screening (PHQ-9) with antidepressant start and follow-up.' },
  SevereDepressionWithSuicidalIdeation: { group: 'Mental health', blurb: 'Severe depression with suicidal ideation and crisis-level intervention.' },

  // Surgical (SurgicalPathwayScenario)
  CholecystectomyPathway: { group: 'Surgical', blurb: 'Gallbladder disease through laparoscopic cholecystectomy and recovery.' },
  TotalKneeReplacementPathway: { group: 'Surgical', blurb: 'Knee osteoarthritis through total knee arthroplasty and rehab.' },

  // Obstetric (PregnantPatientScenario)
  PregnantPatient: { group: 'Obstetric', blurb: 'Prenatal care course across pregnancy with routine obstetric monitoring.' },
};

/** Splits a PascalCase id like `DiabeticPatient` into `Diabetic Patient` for display fallbacks. */
function humanize(scenarioId: string): string {
  return scenarioId
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/([A-Z]+)([A-Z][a-z])/g, '$1 $2')
    .trim();
}

/**
 * Returns the display group + blurb for a scenario id, falling back to a humanized
 * name and a generic group when the id is not in the curated map.
 *
 * Group resolution is a three-tier priority: the `category` argument (the library's
 * own `Category`, when reported) wins if present; otherwise the curated map's `group`
 * is used; otherwise the literal `'Scenario'` fallback applies. `blurb` always comes
 * from the curated map, with a generic fallback text when the id is unlisted.
 */
export function describeScenario(
  scenarioId: string,
  category?: string | null,
): { group: string; label: string; blurb: string } {
  const curated = SCENARIO_DESCRIPTIONS[scenarioId];
  return {
    group: category ?? curated?.group ?? 'Scenario',
    label: humanize(scenarioId),
    blurb: curated?.blurb ?? 'Predefined clinical scenario.',
  };
}
