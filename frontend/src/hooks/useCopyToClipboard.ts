import { useEffect, useRef, useState } from 'react';

/** Copies `text` to the clipboard and reports success as `copied` for `durationMs`, then resets. */
export function useCopyToClipboard(text: string, durationMs: number): { copied: boolean; copy: () => void } {
  const [copied, setCopied] = useState(false);
  const timeoutRef = useRef<number | null>(null);

  useEffect(() => {
    return () => {
      if (timeoutRef.current !== null) {
        window.clearTimeout(timeoutRef.current);
      }
    };
  }, []);

  const copy = () => {
    navigator.clipboard?.writeText(text).then(
      () => {
        setCopied(true);
        if (timeoutRef.current !== null) {
          window.clearTimeout(timeoutRef.current);
        }
        timeoutRef.current = window.setTimeout(() => setCopied(false), durationMs);
      },
      () => undefined,
    );
  };

  return { copied, copy };
}
