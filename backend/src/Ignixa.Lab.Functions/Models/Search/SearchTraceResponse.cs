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

/// <summary><see cref="DataType"/> is the parameter's own resolved <c>Ignixa.Search.Models.SearchParamType</c>
/// (e.g. "String", "Token", "Date", "Reference", "Quantity", "Composite") — mirrors
/// <see cref="Ignixa.Search.Parsing.ParameterTrace.DataType"/> directly. Null when the parameter never
/// bound a value (an `Ignored`/`Failed` outcome). A chain reports its reference parameter's type
/// ("Reference"); a composite reports its own declared type ("Composite"), not one component's.</summary>
public sealed record ParameterTraceDto(
    int Ordinal,
    string Key,
    string Value,
    SyntaxNodeDto? KeySyntax,
    SyntaxNodeDto? ValueSyntax,
    IReadOnlyList<IrRowDto> Ir,
    string? DataType,
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

/// <summary>Mirrors <see cref="Ignixa.Search.Sql.Ast.PlanExplainRow"/> field-for-field. <see cref="Label"/>
/// is display text only (e.g. the match/output CTE prints as <c>"root"</c>); <see cref="CanonicalLabel"/>
/// is the identifier this row shares with its <see cref="SqlTextRangeDto"/> and
/// <see cref="CteProvenanceDto"/> — join on that, never on <see cref="Label"/>. <see cref="Kind"/> is the
/// row's <c>Ignixa.Search.Sql.Ast.PlanRowKind</c> token. <see cref="ReferencedCteIndexes"/> lists the CTEs
/// a structural row (Intersect/Union/Except/ChainJoin) composes, in the order it names them.</summary>
public sealed record PlanExplainRowDto(
    string Label,
    string CanonicalLabel,
    string Kind,
    string Body,
    IReadOnlyList<int> ReferencedCteIndexes);

/// <summary><see cref="ContributingOrdinals"/> is every parameter ordinal this CTE draws from — itself
/// alone when <see cref="ParameterOrdinal"/> is set, or the closed-over union of its children's sets for a
/// structural CTE (Intersect/Union/Except/ChainJoin), or empty where nothing is attributable.</summary>
public sealed record CteProvenanceDto(int CteIndex, int? ParameterOrdinal, IReadOnlyList<int> ContributingOrdinals, SpanDto? Span);

public sealed record EmittedSqlDto(string Sql, IReadOnlyList<SqlTextRangeDto> Ranges);

/// <summary><see cref="Label"/> says which section this is (unique within one emitted statement) and,
/// where a <see cref="PlanExplainRowDto"/> exists for it, equals that row's <see cref="PlanExplainRowDto.CanonicalLabel"/>.
/// <see cref="Kind"/> is the row's <c>Ignixa.Search.Sql.Builders.SqlRangeKind</c> token — set for every
/// range, including the structural ones with no row at all (matchPage/where/seek/orderBy/assembly).</summary>
public sealed record SqlTextRangeDto(string Label, string Kind, int Start, int Length);

public sealed record ImplicitParameterDto(string Name, string Value, string Reason);

public sealed record TraceFailureDto(string Stage, string Message, SpanDto? Span);
