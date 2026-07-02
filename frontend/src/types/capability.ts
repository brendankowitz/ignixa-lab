/**
 * TypeScript mirror of the backend capability DTO (`GET /api/capability`),
 * matching `Ignixa.Lab.Functions.Models.CapabilityResponse` /
 * `CapabilityResourceDto` field-for-field (already camelCase on the wire).
 */

/** A target server's declared capabilities, normalized to the fixed interaction column set. */
export interface CapabilitySummary {
  target: string;
  fhirVersion: string;
  resources: CapabilityResource[];
}

/** A single resource type's declared interactions, as parsed from the target's CapabilityStatement. */
export interface CapabilityResource {
  type: string;
  interactions: string[];
}
