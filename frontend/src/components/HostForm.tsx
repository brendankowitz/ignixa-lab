import { useState } from 'react';

/** Props for {@link HostForm}. */
export interface HostFormProps {
  /** Whether a run is currently in progress (disables the submit control). */
  running: boolean;
  /** Whether at least one suite is selected. */
  canSubmit: boolean;
  /** Invoked with the entered target URL when the form is submitted. */
  onSubmit: (targetUrl: string) => void;
  /** Invoked to cancel an in-flight run. */
  onCancel: () => void;
}

/**
 * Captures the target FHIR server URL and starts/cancels a run.
 *
 * Placeholder layout — visual design is handled separately.
 */
export function HostForm({ running, canSubmit, onSubmit, onCancel }: HostFormProps) {
  const [targetUrl, setTargetUrl] = useState('');

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    const trimmed = targetUrl.trim();
    if (trimmed) {
      onSubmit(trimmed);
    }
  };

  return (
    <form className="host-form" onSubmit={handleSubmit}>
      <label htmlFor="target-url">FHIR server base URL</label>
      <input
        id="target-url"
        name="target-url"
        type="url"
        inputMode="url"
        placeholder="https://hapi.fhir.org/baseR4"
        value={targetUrl}
        onChange={(event) => setTargetUrl(event.target.value)}
        disabled={running}
        required
      />
      {running ? (
        <button type="button" onClick={onCancel}>
          Cancel
        </button>
      ) : (
        <button type="submit" disabled={!canSubmit || targetUrl.trim() === ''}>
          Run suites
        </button>
      )}
    </form>
  );
}
