import { useEffect, useReducer, useRef } from 'react';

export type CopyStatus = 'idle' | 'copied' | 'failed';

export type CopyStatusEvent = { type: 'copied' } | { type: 'failed' } | { type: 'reset' };

export function copyStatusReducer(_status: CopyStatus, event: CopyStatusEvent): CopyStatus {
  switch (event.type) {
    case 'copied':
      return 'copied';
    case 'failed':
      return 'failed';
    case 'reset':
      return 'idle';
  }
}

function clearCopyFeedbackTimer(timeoutRef: { current: ReturnType<typeof setTimeout> | null }) {
  if (timeoutRef.current !== null) {
    clearTimeout(timeoutRef.current);
    timeoutRef.current = null;
  }
}

function useCopyFeedback(text: string, durationMs: number) {
  const [status, dispatch] = useReducer(copyStatusReducer, 'idle' as CopyStatus);
  const timeoutRef = useRef<ReturnType<typeof setTimeout> | null>(null);
  const previousTextRef = useRef(text);

  const resetFeedback = () => {
    clearCopyFeedbackTimer(timeoutRef);
    dispatch({ type: 'reset' });
  };

  const showFeedback = (event: Exclude<CopyStatusEvent, { type: 'reset' }>) => {
    clearCopyFeedbackTimer(timeoutRef);
    dispatch(event);
    timeoutRef.current = setTimeout(() => {
      timeoutRef.current = null;
      dispatch({ type: 'reset' });
    }, durationMs);
  };

  useEffect(() => {
    return () => {
      clearCopyFeedbackTimer(timeoutRef);
    };
  }, []);

  useEffect(() => {
    if (previousTextRef.current !== text) {
      previousTextRef.current = text;
      resetFeedback();
    }
  }, [text]);

  return { status, showFeedback };
}

/** Copies `text` to the clipboard and reports success or failure temporarily. */
export function useCopyToClipboard(
  text: string,
  durationMs: number,
): { copied: boolean; failed: boolean; copy: () => void } {
  const { status, showFeedback } = useCopyFeedback(text, durationMs);

  const copy = () => {
    const writeText = globalThis.navigator?.clipboard?.writeText;
    if (!writeText) {
      showFeedback({ type: 'failed' });
      return;
    }

    writeText.call(globalThis.navigator.clipboard, text).then(
      () => {
        showFeedback({ type: 'copied' });
      },
      () => {
        showFeedback({ type: 'failed' });
      },
    );
  };

  return {
    copied: status === 'copied',
    failed: status === 'failed',
    copy,
  };
}
