import { evaluateMiniFhirPath, parseMiniFhirPath } from '../shared/miniFhirPath';
import { getErrorMessage } from '../shared/errorMessage';

interface SofColumn {
  name: string;
  path: string;
}

type SofUnnest =
  | { forEach: string; forEachOrNull?: never }
  | { forEach?: never; forEachOrNull: string }
  | { forEach?: never; forEachOrNull?: never };

type SofSelect = SofUnnest & {
  column?: SofColumn[];
  select?: SofSelect[];
};

interface SofViewDefinition {
  resource?: string;
  select?: SofSelect[];
}

export type SofCellValue = string | number | boolean | null;

export interface SofRunResult {
  error: string | null;
  columns: string[];
  rows: Record<string, SofCellValue>[];
  meta: string;
}

function toCellValue(values: unknown[]): SofCellValue {
  if (values.length === 0) return null;
  const value = values.length === 1 ? values[0] : values;
  if (typeof value === 'object' && value !== null) return JSON.stringify(value);
  return value as SofCellValue;
}

/** Recursively sets every column name found under `selects` (at any nesting depth) to `null` in `base`. */
function nullifyNestedColumns(selects: SofSelect[], base: Record<string, SofCellValue>): void {
  for (const part of selects) {
    for (const column of part.column ?? []) base[column.name] = null;
    if (part.select) nullifyNestedColumns(part.select, base);
  }
}

function selectRows(selects: SofSelect[], context: unknown): Record<string, SofCellValue>[] {
  let rows: Record<string, SofCellValue>[] = [{}];

  for (const part of selects) {
    if (part.forEach !== undefined && part.forEachOrNull !== undefined) {
      throw new Error('A select cannot have both forEach and forEachOrNull');
    }

    let contexts: unknown[];
    if (part.forEach !== undefined) {
      contexts = evaluateMiniFhirPath(parseMiniFhirPath(part.forEach), [context], { vars: {} });
    } else if (part.forEachOrNull !== undefined) {
      contexts = evaluateMiniFhirPath(parseMiniFhirPath(part.forEachOrNull), [context], { vars: {} });
      if (contexts.length === 0) contexts = [null];
    } else {
      contexts = [context];
    }

    const partRows: Record<string, SofCellValue>[] = [];
    for (const item of contexts) {
      const base: Record<string, SofCellValue> = {};
      for (const column of part.column ?? []) {
        if (item === null) {
          base[column.name] = null;
          continue;
        }
        base[column.name] = toCellValue(evaluateMiniFhirPath(parseMiniFhirPath(column.path), [item], { vars: {} }));
      }
      if (part.select && item !== null) {
        for (const nested of selectRows(part.select, item)) partRows.push({ ...base, ...nested });
      } else {
        if (part.select && item === null) nullifyNestedColumns(part.select, base);
        partRows.push(base);
      }
    }

    const next: Record<string, SofCellValue>[] = [];
    for (const existingRow of rows) for (const partRow of partRows) next.push({ ...existingRow, ...partRow });
    rows = next;
  }

  return rows;
}

function collectColumnNames(selects: SofSelect[] | undefined, out: string[]): void {
  for (const part of selects ?? []) {
    for (const column of part.column ?? []) if (!out.includes(column.name)) out.push(column.name);
    if (part.select) collectColumnNames(part.select, out);
  }
}

/** Flattens `resources` through a ViewDefinition's `select`/`forEach`/`forEachOrNull`/`column` tree (`forEachOrNull` keeps a row with null columns instead of dropping it when the nested collection is empty). */
export function runSof(viewDefinitionText: string, resourcesText: string): SofRunResult {
  let viewDefinition: SofViewDefinition;
  let resources: unknown;
  try {
    viewDefinition = JSON.parse(viewDefinitionText) as SofViewDefinition;
  } catch (error) {
    return { error: `ViewDefinition JSON — ${getErrorMessage(error)}`, columns: [], rows: [], meta: '' };
  }
  if (viewDefinition === null || typeof viewDefinition !== 'object') {
    return { error: 'ViewDefinition JSON must be an object', columns: [], rows: [], meta: '' };
  }
  if (viewDefinition.select !== undefined && !Array.isArray(viewDefinition.select)) {
    return { error: 'ViewDefinition.select must be an array', columns: [], rows: [], meta: '' };
  }
  try {
    resources = JSON.parse(resourcesText);
  } catch (error) {
    return { error: `Resources JSON — ${getErrorMessage(error)}`, columns: [], rows: [], meta: '' };
  }

  const resourceList = Array.isArray(resources) ? resources : [resources];
  const columns: string[] = [];
  const rows: Record<string, SofCellValue>[] = [];
  let scanned = 0;
  try {
    collectColumnNames(viewDefinition.select, columns);
    for (const resource of resourceList) {
      const record = resource as { resourceType?: string };
      if (viewDefinition.resource && record.resourceType !== viewDefinition.resource) continue;
      scanned++;
      rows.push(...selectRows(viewDefinition.select ?? [], resource));
    }
  } catch (error) {
    return { error: getErrorMessage(error), columns, rows: [], meta: '' };
  }

  const meta = `${rows.length} ${rows.length === 1 ? 'row' : 'rows'} · ${columns.length} cols · ${scanned} resources`;
  return { error: null, columns, rows, meta };
}
