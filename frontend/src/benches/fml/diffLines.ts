export interface DiffRow {
  sign: ' ' | '−' | '+';
  text: string;
}

/** Longest-common-subsequence line diff between two texts, mockup-identical algorithm. */
export function diffLines(a: string, b: string): DiffRow[] {
  const linesA = a.split('\n');
  const linesB = b.split('\n');
  const n = linesA.length;
  const m = linesB.length;
  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i][j] = linesA[i] === linesB[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
    }
  }

  const rows: DiffRow[] = [];
  let i = 0;
  let j = 0;
  while (i < n && j < m) {
    if (linesA[i] === linesB[j]) {
      rows.push({ sign: ' ', text: linesA[i] });
      i++;
      j++;
    } else if (dp[i + 1][j] >= dp[i][j + 1]) {
      rows.push({ sign: '−', text: linesA[i] });
      i++;
    } else {
      rows.push({ sign: '+', text: linesB[j] });
      j++;
    }
  }
  while (i < n) rows.push({ sign: '−', text: linesA[i++] });
  while (j < m) rows.push({ sign: '+', text: linesB[j++] });
  return rows;
}
