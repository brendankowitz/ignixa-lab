export interface FmlHighlightSegment {
  text: string;
  color: string;
}

export interface FmlHighlightLine {
  segments: FmlHighlightSegment[];
}

const KEYWORD_PATTERN =
  /^(map|uses|group|source|target|as|alias|imports|extends|where|then|first|not_first|last|not_last|only_one|share|collate|check|log|types|default)$/;

const TOKEN_PATTERN = /("[^"]*")|('[^']*')|(->)|([A-Za-z_][\w]*)|(\d+)|(\s+)|(.)/g;

/** Line-by-line syntax highlighter for the FML editor pane. */
export function highlightFml(text: string): FmlHighlightLine[] {
  return text.split('\n').map((line) => {
    const segments: FmlHighlightSegment[] = [];
    let rest = line;
    let comment: string | null = null;
    const commentIndex = line.indexOf('//');
    if (commentIndex >= 0) {
      comment = line.slice(commentIndex);
      rest = line.slice(0, commentIndex);
    }

    const pattern = new RegExp(TOKEN_PATTERN);
    let match: RegExpExecArray | null;
    while ((match = pattern.exec(rest))) {
      const text2 = match[0];
      let color = 'var(--text2)';
      if (match[1]) color = 'var(--pass)';
      else if (match[2]) color = 'var(--chip-amb-fg)';
      else if (match[3]) color = 'var(--hl-arrow)';
      else if (match[4]) color = KEYWORD_PATTERN.test(text2) ? 'var(--accent)' : 'var(--text)';
      else if (match[5]) color = 'var(--chip-teal-fg)';
      segments.push({ text: text2, color });
    }

    if (comment) segments.push({ text: comment, color: 'var(--text4)' });
    if (segments.length === 0) segments.push({ text: ' ', color: 'var(--text2)' });
    return { segments };
  });
}
