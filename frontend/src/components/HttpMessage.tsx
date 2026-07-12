import { useMemo } from 'react';
import { prettyJson, tokenizeJson } from '../lib/httpFormat';
import type { ConformanceHttpRequest, ConformanceHttpResponse } from '../types/conformance';

/** Marker value the backend substitutes for sensitive header values (see `ConformanceReportMapper`). */
const REDACTED_HEADER_VALUE = '***redacted***';

/** Short reason phrases for common status codes, shown next to the status chip when known. */
const STATUS_REASONS: Record<number, string> = {
  200: 'OK',
  201: 'Created',
  202: 'Accepted',
  204: 'No Content',
  301: 'Moved Permanently',
  302: 'Found',
  304: 'Not Modified',
  400: 'Bad Request',
  401: 'Unauthorized',
  403: 'Forbidden',
  404: 'Not Found',
  405: 'Method Not Allowed',
  409: 'Conflict',
  410: 'Gone',
  412: 'Precondition Failed',
  422: 'Unprocessable Entity',
  429: 'Too Many Requests',
  500: 'Internal Server Error',
  502: 'Bad Gateway',
  503: 'Service Unavailable',
  504: 'Gateway Timeout',
};

/** Renders a captured HTTP request as a start line, headers, and (pretty-printed, colorized) body. */
export function HttpRequestView({ request }: { request: ConformanceHttpRequest }) {
  return (
    <div className="http-message">
      <div className="http-message__start-line">
        <MethodChip method={request.method} />
        <span className="http-message__url">{request.url}</span>
      </div>
      <HeaderList headers={request.headers} />
      <BodyBlock body={request.body} />
    </div>
  );
}

/** Renders a captured HTTP response as a status line, headers, and (pretty-printed, colorized) body. */
export function HttpResponseView({ response }: { response: ConformanceHttpResponse }) {
  return (
    <div className="http-message">
      <div className="http-message__start-line">
        <StatusChip statusCode={response.statusCode} />
        {STATUS_REASONS[response.statusCode] ? (
          <span className="http-message__reason">{STATUS_REASONS[response.statusCode]}</span>
        ) : null}
      </div>
      <HeaderList headers={response.headers} />
      {response.bodyParseError ? (
        <p className="http-message__error">Unparseable body: {response.bodyParseError}</p>
      ) : null}
      <BodyBlock body={response.body} />
    </div>
  );
}

const METHOD_VARIANTS: Record<string, string> = {
  GET: 'get',
  POST: 'post',
  PUT: 'put',
  PATCH: 'patch',
  DELETE: 'delete',
};

function MethodChip({ method }: { method: string }) {
  const normalized = method.toUpperCase();
  const variant = METHOD_VARIANTS[normalized] ?? 'other';
  return <span className={`http-message__method http-message__method--${variant}`}>{normalized}</span>;
}

function StatusChip({ statusCode }: { statusCode: number }) {
  return (
    <span className={`http-message__status http-message__status--${statusVariant(statusCode)}`}>{statusCode}</span>
  );
}

export function statusVariant(statusCode: number): 'success' | 'redirect' | 'warn' | 'fail' | 'neutral' {
  if (statusCode >= 200 && statusCode < 300) return 'success';
  if (statusCode >= 300 && statusCode < 400) return 'redirect';
  if (statusCode >= 400 && statusCode < 500) return 'warn';
  if (statusCode >= 500) return 'fail';
  return 'neutral';
}

function HeaderList({ headers }: { headers: Record<string, string> }) {
  const entries = Object.entries(headers);
  if (entries.length === 0) {
    return null;
  }
  return (
    <div className="http-message__headers">
      {entries.map(([name, value]) => (
        <div key={name} className="http-message__header">
          <span className="http-message__header-name">{name}</span>
          <span
            className={`http-message__header-value${value === REDACTED_HEADER_VALUE ? ' http-message__header-value--redacted' : ''}`}
          >
            {value}
          </span>
        </div>
      ))}
    </div>
  );
}

function BodyBlock({ body }: { body: string | null }) {
  if (body === null || body.trim().length === 0) {
    return null;
  }
  const { text, isJson } = prettyJson(body);
  return <pre className="http-message__body">{isJson ? <JsonCode text={text} /> : text}</pre>;
}

function JsonCode({ text }: { text: string }) {
  const tokens = useMemo(() => tokenizeJson(text), [text]);
  return (
    <>
      {tokens.map((token, index) => (
        <span key={index} className={`tok tok--${token.cls}`}>
          {token.value}
        </span>
      ))}
    </>
  );
}
