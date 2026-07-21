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

/// <summary><see cref="DataType"/> is the parameter's own resolved <c>Ignixa.Search.Models.SearchParameterInfo.Type</c>
/// (e.g. "String", "Token", "Date", "Reference", "Quantity", "Composite") — null when <see cref="Ir"/> has
/// no search-parameter-bearing node to read it from (an `Ignored`/`Failed` outcome with no successful
/// parse). A chain reports its reference parameter's type ("Reference"); a composite reports its own
/// declared type ("Composite"), not one component's.</summary>
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

/// <summary><see cref="Kind"/> is the row's <c>Ignixa.Search.Sql.Ast.CteDefinition</c> case name for a
/// CTE row (ParamSource/Intersect/Union/Except/ChainJoin/CompartmentSource/ResourceSource), or a
/// result-shape modifier's own name (Sort/Page/Include/CountOnly) for a non-CTE row — see
/// <see cref="Ignixa.Lab.Functions.Services.Search.PlanRowKindClassifier"/>, which derives it from
/// <see cref="Label"/>/<see cref="Body"/>
/// since the engine's own <c>PlanExplainRow</c> does not carry it as a separate field.</summary>
/// <summary><see cref="CteIndex"/> is this row's real 0-based index into <see cref="QueryPlanDto.Ctes"/>,
/// or null for a non-CTE row (Sort/Page/Include/CountOnly). It exists because the engine's own
/// <c>PlanExplainer</c> relabels whichever CTE is the plan's match/output as <c>"root"</c> instead of
/// <c>"cte{i}"</c> — <see cref="Label"/> alone is therefore not always enough to look this row up in
/// <see cref="QueryPlanDto.Ctes"/> (needed for click-to-trace and for a structural row like ChainJoin,
/// whose own <see cref="CteProvenanceDto.ParameterOrdinal"/> is null, to inherit an ordinal from the CTEs
/// it references).</summary>
public sealed record PlanExplainRowDto(string Label, string Kind, int? CteIndex, string Body);

public sealed record CteProvenanceDto(int CteIndex, int? ParameterOrdinal, SpanDto? Span);

public sealed record EmittedSqlDto(string Sql, IReadOnlyList<SqlTextRangeDto> Ranges);

public sealed record SqlTextRangeDto(string Label, int Start, int Length);

public sealed record ImplicitParameterDto(string Name, string Value, string Reason);

public sealed record TraceFailureDto(string Stage, string Message, SpanDto? Span);
