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
  const requestIdRef = useRef(0);

  const resetFeedback = () => {
    clearCopyFeedbackTimer(timeoutRef);
    dispatch({ type: 'reset' });
  };

  const showFeedback = (event: Exclude<CopyStatusEvent, { type: 'reset' }>, requestId: number) => {
    if (requestId !== requestIdRef.current) {
      return;
    }

    clearCopyFeedbackTimer(timeoutRef);
    dispatch(event);
    timeoutRef.current = setTimeout(() => {
      if (requestId !== requestIdRef.current) {
        return;
      }

      timeoutRef.current = null;
      dispatch({ type: 'reset' });
    }, durationMs);
  };

  useEffect(() => {
    return () => {
      requestIdRef.current += 1;
      clearCopyFeedbackTimer(timeoutRef);
    };
  }, []);

  useEffect(() => {
    if (previousTextRef.current !== text) {
      previousTextRef.current = text;
      requestIdRef.current += 1;
      resetFeedback();
    }
  }, [text]);

  return { status, showFeedback, requestIdRef };
}

/** Copies `text` to the clipboard and reports success or failure temporarily. */
export function useCopyToClipboard(
  text: string,
  durationMs: number,
): { copied: boolean; failed: boolean; copy: () => void } {
  const { status, showFeedback, requestIdRef } = useCopyFeedback(text, durationMs);

  const copy = () => {
    const requestId = requestIdRef.current + 1;
    requestIdRef.current = requestId;
    const writeText = globalThis.navigator?.clipboard?.writeText;
    if (!writeText) {
      showFeedback({ type: 'failed' }, requestId);
      return;
    }

    writeText.call(globalThis.navigator.clipboard, text).then(
      () => {
        showFeedback({ type: 'copied' }, requestId);
      },
      () => {
        showFeedback({ type: 'failed' }, requestId);
      },
    );
  };

  return {
    copied: status === 'copied',
    failed: status === 'failed',
    copy,
  };
}
