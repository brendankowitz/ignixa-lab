export type ValidationDepth = 'minimal' | 'spec' | 'full' | 'compatibility';
export type ValidationSeverity = 'fatal' | 'error' | 'warning' | 'information';

export interface ValidationRequest {
  fhirVersion: string;
  depth: ValidationDepth;
  skipTerminology: boolean;
  packages: string[];
  resource: unknown;
}

export interface ValidationSummary {
  fatal: number;
  error: number;
  warning: number;
  information: number;
  total: number;
}

export interface ValidationIssue {
  severity: ValidationSeverity;
  code: string;
  path: string;
  message: string;
  details?: string | null;
}

export interface ValidationResponse {
  fhirVersion: string;
  engineVersion: string;
  resourceType: string;
  depth: ValidationDepth;
  isValid: boolean;
  summary: ValidationSummary;
  issues: ValidationIssue[];
}
