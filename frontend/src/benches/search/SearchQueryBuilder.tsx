import type { CSSProperties, MouseEvent } from 'react';
import { monoFont } from '../components/styles';
import { appendUnique, parseQueryString, removePair, setSort, upsertSingleton, type SortField } from './queryBuilder';
import type { ResourceType } from './searchTypes';

/** Sortable fields per resource type, deliberately restricted to String/Date-typed search parameters (plus
 * `_lastUpdated`) — verified live against the real compiler. `Ignixa.Search.Sql` (alpha) only supports
 * sorting by String, Date, and `_lastUpdated` keys today; Token/Number/Quantity/Reference/Uri sort throws
 * at Lower, so those types are deliberately excluded from the picker rather than offered and failing. */
const SORTABLE_FIELDS: Record<ResourceType, string[]> = {
  Patient: ['name', 'birthdate', 'address-city', '_lastUpdated'],
  Observation: ['date', '_lastUpdated'],
  Encounter: ['date', '_lastUpdated'],
};

interface RefChipOption {
  key: string;
  value: string;
  label: string;
}

// Every search-term chip below was run against the real compiler before being added here (not merely
// plausible-looking FHIR syntax) — each compiles cleanly (no per-parameter Ignored/Failed outcome, no
// page-level Failure). Two are deliberately excluded even though they're valid FHIR search syntax:
// `system|code` token search and `:contains`/`:exact` string modifiers on values that could overflow the
// inline column — `Ignixa.Search.Sql` (alpha) doesn't support either yet, so both would render as a
// per-parameter Failed outcome instead of demonstrating a working trace. Chips share a `key` (e.g. the two
// `date` chips on Observation) so they combine as an AND, same as typing both by hand — clicking both
// toggles both `date=` pairs on independently, exactly like `_include`.
const SEARCH_TERMS: Record<ResourceType, RefChipOption[]> = {
  Patient: [
    { key: 'name', value: 'Smith', label: 'name=Smith' },
    { key: 'gender', value: 'male', label: 'gender=male' },
    { key: 'birthdate', value: 'gt2000-01-01', label: 'birthdate=gt2000-01-01' },
    { key: 'name', value: 'Smith,Jones', label: 'name=Smith,Jones (OR)' },
    { key: 'general-practitioner:Practitioner.name', value: 'Jones', label: 'general-practitioner.name=Jones (chain)' },
    { key: '_has:Observation:patient:code', value: '1234-5', label: '_has Observation.patient.code (reverse chain)' },
  ],
  Observation: [
    { key: 'code', value: '8480-6', label: 'code=8480-6' },
    { key: 'code-value-quantity', value: '8480-6$gt90', label: 'code-value-quantity=8480-6$gt90 (composite)' },
    { key: 'patient', value: 'Patient/123', label: 'patient=Patient/123' },
    { key: 'date', value: 'ge2024-01-01', label: 'date=ge2024-01-01' },
    { key: 'date', value: 'lt2025-01-01', label: 'date=lt2025-01-01' },
  ],
  Encounter: [
    { key: 'status', value: 'finished', label: 'status=finished' },
    { key: 'date', value: 'ge2024-01-01', label: 'date=ge2024-01-01' },
    { key: 'subject.name', value: 'Smith', label: 'subject.name=Smith (chain)' },
    { key: 'class', value: 'AMB', label: 'class=AMB' },
  ],
};

/** `_include`/`_include:iterate` chip options, verified live — one plain and one `:iterate` variant per
 * curated reference target, so both are one click each rather than a separate modifier control. Curated
 * rather than schema-driven (see the design discussion): a reference search parameter worth demonstrating
 * the `_include` result-shape modifier, not an exhaustive list of the type's actual reference parameters. */
const INCLUDABLE: Record<ResourceType, RefChipOption[]> = {
  Patient: [
    { key: '_include', value: 'Patient:general-practitioner', label: 'general-practitioner' },
    { key: '_include:iterate', value: 'Patient:general-practitioner', label: 'general-practitioner (iterate)' },
  ],
  Observation: [
    { key: '_include', value: 'Observation:patient', label: 'patient' },
    { key: '_include:iterate', value: 'Observation:patient', label: 'patient (iterate)' },
  ],
  Encounter: [
    { key: '_include', value: 'Encounter:subject', label: 'subject' },
    { key: '_include:iterate', value: 'Encounter:subject', label: 'subject (iterate)' },
  ],
};

/** `_revinclude`/`_revinclude:iterate` chip options, verified live: another resource type's reference
 * parameter that points AT this resource type. */
const REVINCLUDABLE: Record<ResourceType, RefChipOption[]> = {
  Patient: [
    { key: '_revinclude', value: 'Observation:patient', label: 'Observation.patient' },
    { key: '_revinclude:iterate', value: 'Observation:patient', label: 'Observation.patient (iterate)' },
  ],
  Observation: [
    { key: '_revinclude', value: 'DiagnosticReport:result', label: 'DiagnosticReport.result' },
    { key: '_revinclude:iterate', value: 'DiagnosticReport:result', label: 'DiagnosticReport.result (iterate)' },
  ],
  Encounter: [
    { key: '_revinclude', value: 'Observation:encounter', label: 'Observation.encounter' },
    { key: '_revinclude:iterate', value: 'Observation:encounter', label: 'Observation.encounter (iterate)' },
  ],
};

const SUMMARY_VALUES = ['true', 'false', 'text', 'data', 'count'];
const TOTAL_VALUES = ['accurate', 'none'];

function toggleChipStyle(active: boolean): CSSProperties {
  return {
    fontFamily: monoFont,
    fontSize: 10.5,
    padding: '5px 10px',
    borderRadius: 99,
    border: `1px solid ${active ? 'var(--accent)' : 'var(--border)'}`,
    color: active ? 'var(--accent-contrast)' : 'var(--text2)',
    background: active ? 'var(--accent)' : 'var(--panel)',
    cursor: 'pointer',
  };
}

function ToggleChip({ label, active, onClick }: { label: string; active: boolean; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} style={toggleChipStyle(active)}>
      {label}
    </button>
  );
}

function currentSortFields(query: string): SortField[] {
  const pair = parseQueryString(query).find((p) => p.key === '_sort');
  if (!pair || pair.value.trim().length === 0) {
    return [];
  }
  return pair.value
    .split(',')
    .filter((f) => f.length > 0)
    .map((f) => (f.startsWith('-') ? { field: f.slice(1), descending: true } : { field: f, descending: false }));
}

/** A "click to add, click again to remove" builder covering the whole query: search terms (`name=Smith`,
 * a chain, a composite, …) plus the control parameters the engine supports beyond plain search —
 * `_sort` (up to 3 String/Date keys), `_include`/`_revinclude` (with `:iterate`), and `_summary`/`_total`.
 * This is the only way to compose a query in the bench (no separate "Examples" list) — every chip is a
 * real, individually toggleable fragment that combines with any other. Every value offered here was run
 * against the real compiler first (see `SEARCH_TERMS`/`SORTABLE_FIELDS`/`INCLUDABLE`/`REVINCLUDABLE`
 * above), so toggling a chip on should never land on a Failed/Ignored outcome. `_count` is deliberately not
 * offered: `SearchCompiler.CompileAsync` doesn't wire `MaxItemCount` through to `Lower.Run`, so it has no
 * visible effect on the plan/SQL this bench traces. */
export function SearchQueryBuilder({
  resourceType,
  query,
  onQueryChange,
}: {
  resourceType: ResourceType;
  query: string;
  onQueryChange: (query: string) => void;
}) {
  const sortFields = currentSortFields(query);
  const activePairs = parseQueryString(query);

  const toggleSort = (field: string) => {
    const existing = sortFields.find((f) => f.field === field);
    if (existing) {
      onQueryChange(setSort(query, sortFields.filter((f) => f.field !== field)));
    } else if (sortFields.length < 3) {
      onQueryChange(setSort(query, [...sortFields, { field, descending: false }]));
    }
  };

  const flipSortDirection = (event: MouseEvent, field: string) => {
    event.stopPropagation();
    onQueryChange(setSort(query, sortFields.map((f) => (f.field === field ? { ...f, descending: !f.descending } : f))));
  };

  const toggleRefPair = (key: string, value: string) => {
    const active = activePairs.some((p) => p.key === key && p.value === value);
    onQueryChange(active ? removePair(query, key, value) : appendUnique(query, key, value));
  };

  const toggleSingleton = (key: string, value: string) => {
    const current = activePairs.find((p) => p.key === key)?.value;
    onQueryChange(upsertSingleton(query, key, current === value ? '' : value));
  };

  return (
    <div style={{ display: 'flex', flexDirection: 'column', gap: 6 }}>
      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
        <span style={{ fontSize: 11, color: 'var(--text4)', minWidth: 62 }}>Search</span>
        {SEARCH_TERMS[resourceType].map((opt) => (
          <ToggleChip
            key={`${opt.key}=${opt.value}`}
            label={opt.label}
            active={activePairs.some((p) => p.key === opt.key && p.value === opt.value)}
            onClick={() => toggleRefPair(opt.key, opt.value)}
          />
        ))}
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
        <span style={{ fontSize: 11, color: 'var(--text4)', minWidth: 62 }}>Include</span>
        {INCLUDABLE[resourceType].map((opt) => (
          <ToggleChip
            key={`${opt.key}=${opt.value}`}
            label={opt.label}
            active={activePairs.some((p) => p.key === opt.key && p.value === opt.value)}
            onClick={() => toggleRefPair(opt.key, opt.value)}
          />
        ))}
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
        <span style={{ fontSize: 11, color: 'var(--text4)', minWidth: 62 }}>RevInclude</span>
        {REVINCLUDABLE[resourceType].map((opt) => (
          <ToggleChip
            key={`${opt.key}=${opt.value}`}
            label={opt.label}
            active={activePairs.some((p) => p.key === opt.key && p.value === opt.value)}
            onClick={() => toggleRefPair(opt.key, opt.value)}
          />
        ))}
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
        <span style={{ fontSize: 11, color: 'var(--text4)', minWidth: 62 }}>Sort</span>
        {SORTABLE_FIELDS[resourceType].map((field) => {
          const active = sortFields.find((f) => f.field === field);
          return (
            <ToggleChip
              key={field}
              active={active !== undefined}
              onClick={() => toggleSort(field)}
              label={active ? `${field} ${active.descending ? '▼' : '▲'}` : field}
            />
          );
        })}
        {sortFields.map((f) => (
          <span
            key={`flip-${f.field}`}
            onClick={(event) => flipSortDirection(event, f.field)}
            title={`Flip ${f.field} to ${f.descending ? 'ascending' : 'descending'}`}
            style={{ fontSize: 10.5, color: 'var(--text4)', cursor: 'pointer' }}
          >
            ⇅ {f.field}
          </span>
        ))}
      </div>

      <div style={{ display: 'flex', flexWrap: 'wrap', alignItems: 'center', gap: 6 }}>
        <span style={{ fontSize: 11, color: 'var(--text4)', minWidth: 62 }}>_summary</span>
        {SUMMARY_VALUES.map((value) => (
          <ToggleChip
            key={value}
            label={value}
            active={activePairs.some((p) => p.key === '_summary' && p.value === value)}
            onClick={() => toggleSingleton('_summary', value)}
          />
        ))}
        <span style={{ fontSize: 11, color: 'var(--text4)', minWidth: 40, marginLeft: 6 }}>_total</span>
        {TOTAL_VALUES.map((value) => (
          <ToggleChip
            key={value}
            label={value}
            active={activePairs.some((p) => p.key === '_total' && p.value === value)}
            onClick={() => toggleSingleton('_total', value)}
          />
        ))}
        <div style={{ flex: 1 }} />
        {query.length > 0 ? (
          <span
            onClick={() => onQueryChange('')}
            title="Clear the entire query"
            style={{ fontFamily: monoFont, fontSize: 11, fontWeight: 600, color: 'var(--text3)', cursor: 'pointer' }}
          >
            ✕ clear
          </span>
        ) : null}
      </div>
    </div>
  );
}
