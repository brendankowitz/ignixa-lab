import { evaluateMiniFhirPath, parseMiniFhirPath } from '../shared/miniFhirPath';
import { getErrorMessage } from '../shared/errorMessage';

export type FmlRuleStatus = 'applied' | 'skipped' | 'error';

export interface FmlLogRow {
  rule: string;
  group: string;
  src: string;
  tgt: string;
  val: string;
  status: FmlRuleStatus;
}

export interface FmlRunResult {
  error: string | null;
  log: FmlLogRow[];
  output: Record<string, unknown> | null;
  mapName: string;
  applied: number;
  skipped: number;
}

function setPath(target: Record<string, unknown>, path: string, value: unknown): void {
  const parts = path.split('.');
  let cursor: Record<string, unknown> = target;
  for (let i = 0; i < parts.length - 1; i++) {
    const key = parts[i];
    if (typeof cursor[key] !== 'object' || cursor[key] === null) cursor[key] = {};
    cursor = cursor[key] as Record<string, unknown>;
  }
  cursor[parts[parts.length - 1]] = value;
}

const GROUP_PATTERN =
  /group\s+(\w+)\s*\(\s*source\s+(\w+)\s*(?::\s*(\w+))?\s*,\s*target\s+(\w+)\s*(?::\s*(\w+))?\s*\)\s*\{([\s\S]*?)\}/g;

const RULE_PATTERN = /^(\w+)(?:\.([\w.]+))?(?:\s+as\s+(\w+))?\s*->\s*(\w+)\.([\w.]+)(?:\s*=\s*(.+))?$/;

/** Applies a small StructureMap-subset (`group ... { rules }`) against a source resource, producing a target object and a per-rule execution log. Mockup-identical mini-interpreter. */
export function runFml(mapText: string, sourceText: string): FmlRunResult {
  let source: unknown;
  try {
    source = JSON.parse(sourceText);
  } catch (error) {
    return { error: `Source JSON — ${getErrorMessage(error)}`, log: [], output: null, mapName: '', applied: 0, skipped: 0 };
  }

  const output: Record<string, unknown> = {};
  const log: FmlLogRow[] = [];
  let mapName = '(unnamed map)';
  let applied = 0;
  let skipped = 0;
  let foundGroup = false;

  const mapNameMatch = mapText.match(/map\s+"[^"]*"\s*=\s*"([^"]+)"/);
  if (mapNameMatch) mapName = mapNameMatch[1];

  try {
    const groupPattern = new RegExp(GROUP_PATTERN);
    let groupMatch: RegExpExecArray | null;
    while ((groupMatch = groupPattern.exec(mapText))) {
      foundGroup = true;
      const groupName = groupMatch[1];
      const sourceType = groupMatch[3] ?? 'src';
      const targetType = groupMatch[5] ?? 'tgt';
      const body = groupMatch[6];
      if (groupMatch[5] && !output.resourceType) output.resourceType = groupMatch[5];

      for (const rawLine of body.split('\n')) {
        const noComment = rawLine.replace(/\/\/.*$/, '').trim();
        if (!noComment || !noComment.endsWith(';')) continue;
        const line = noComment.slice(0, -1).trim();
        const ruleMatch = line.match(RULE_PATTERN);
        if (!ruleMatch) {
          log.push({ rule: '(parse)', group: groupName, src: line.slice(0, 40), tgt: '', val: '', status: 'error' });
          continue;
        }

        const [, , srcPath, alias, , tgtPath, rhsRaw] = ruleMatch;
        let rhs = (rhsRaw ?? '').trim();
        let ruleName = '(unnamed)';
        const ruleNameMatch = rhs.match(/"([^"]+)"\s*$/);
        if (ruleNameMatch) {
          ruleName = ruleNameMatch[1];
          rhs = rhs.slice(0, ruleNameMatch.index).trim();
        }

        let sourceValues: unknown[] = [source];
        if (srcPath) sourceValues = evaluateMiniFhirPath(parseMiniFhirPath(srcPath), [source], { vars: {} });

        let value: unknown;
        if (!rhs || rhs === alias) {
          value = sourceValues.length === 0 ? undefined : sourceValues.length === 1 ? sourceValues[0] : sourceValues;
        } else if (/^'.*'$/.test(rhs)) {
          value = rhs.slice(1, -1);
        } else {
          const truncateMatch = rhs.match(/^truncate\(\s*(\w+)\s*,\s*(\d+)\s*\)$/);
          if (truncateMatch && truncateMatch[1] === alias) {
            const first = sourceValues[0];
            value = first === undefined ? undefined : String(first).slice(0, Number.parseInt(truncateMatch[2], 10));
          } else {
            value = sourceValues.length ? (sourceValues.length === 1 ? sourceValues[0] : sourceValues) : undefined;
          }
        }

        const srcLabel = srcPath ? `${sourceType}.${srcPath}` : sourceType;
        const tgtLabel = `${targetType}.${tgtPath}`;
        if (value === undefined) {
          skipped++;
          log.push({ rule: ruleName, group: groupName, src: srcLabel, tgt: tgtLabel, val: '—', status: 'skipped' });
        } else {
          setPath(output, tgtPath, value);
          applied++;
          let valuePreview = JSON.stringify(value);
          if (valuePreview.length > 48) valuePreview = `${valuePreview.slice(0, 48)}…`;
          log.push({ rule: ruleName, group: groupName, src: srcLabel, tgt: tgtLabel, val: valuePreview, status: 'applied' });
        }
      }
    }
  } catch (error) {
    return { error: getErrorMessage(error), log, output, mapName, applied, skipped };
  }

  if (!foundGroup) {
    return {
      error: 'No group found. Expected: group Name(source src : Type, target tgt : Type) { … }',
      log: [],
      output: null,
      mapName,
      applied: 0,
      skipped: 0,
    };
  }

  return { error: null, log, output, mapName, applied, skipped };
}
