namespace Ignixa.Lab.Functions.Models.Search;

/// <summary>Serializable projection of <see cref="Ignixa.Search.Sql.Tracing.SearchTrace"/> for the Search
/// bench UI. Mirrors the trace field-for-field, replacing the two non-serializable pieces (the live IR
/// <c>Expression</c> graph and the plan's raw expression graph) with flattened row projections. Serialized
/// as camelCase JSON (ASP.NET Core default).</summary>
public sealed record SearchTraceResponse(
    string ResourceType,
    IReadOnlyList<ParameterTraceDto> Parameters,
    QueryPlanDto? Plan,
    EmittedSqlDto? Sql,
    IReadOnlyList<ImplicitParameterDto> Implicit,
    TraceFailureDto? Failure);

public sealed record ParameterTraceDto(
    int Ordinal,
    string Key,
    string Value,
    SyntaxNodeDto? KeySyntax,
    SyntaxNodeDto? ValueSyntax,
    IReadOnlyList<IrRowDto> Ir,
    ParameterOutcomeDto Outcome);

/// <summary>Serializable <see cref="Ignixa.Search.Expressions.Parsers.SyntaxNode"/>. Its <see cref="Span"/>
/// is non-null (the syntax scanner always spans real text).</summary>
public sealed record SyntaxNodeDto(string Kind, SpanDto Span, IReadOnlyList<SyntaxNodeDto> Children);

/// <summary>A range within one parameter's key or value string. <see cref="Origin"/> is "Key" or "Value";
/// <see cref="Start"/>/<see cref="Length"/> index into that string, NOT the whole query string.</summary>
public sealed record SpanDto(string Origin, int Start, int Length);

public sealed record IrRowDto(string Kind, string Text, int Depth);

/// <summary><see cref="Kind"/> is "Compiled" | "Ignored" | "Failed". <see cref="Reason"/> carries the
/// Ignored reason or the Failed message; <see cref="Stage"/> is set only for Failed.</summary>
public sealed record ParameterOutcomeDto(string Kind, string? Reason, string? Stage, SpanDto? Span);

public sealed record QueryPlanDto(string Explain, IReadOnlyList<PlanExplainRowDto> Rows, IReadOnlyList<CteProvenanceDto> Ctes);

public sealed record PlanExplainRowDto(string Label, string Body);

public sealed record CteProvenanceDto(int CteIndex, int? ParameterOrdinal, SpanDto? Span);

public sealed record EmittedSqlDto(string Sql, IReadOnlyList<SqlTextRangeDto> Ranges);

public sealed record SqlTextRangeDto(string Label, int Start, int Length);

public sealed record ImplicitParameterDto(string Name, string Value, string Reason);

public sealed record TraceFailureDto(string Stage, string Message, SpanDto? Span);
