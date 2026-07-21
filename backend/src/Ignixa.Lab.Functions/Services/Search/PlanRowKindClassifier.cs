namespace Ignixa.Lab.Functions.Services.Search;

/// <summary>
/// Derives a <c>PlanExplainRowDto.Kind</c> from a <c>PlanExplainRow</c>'s label/body text. The engine's
/// <c>QueryPlanTrace.Rows</c> only carries the already-flattened human-readable strings
/// <c>Ignixa.Search.Sql.Ast.PlanExplainer</c> prints — not the underlying <c>CteDefinition</c>/result-shape
/// type the row came from — so the Search bench UI (which wants a kind chip per row, matching how the
/// Search Expression column chips its <c>IrRow.Kind</c>) has nothing to read that from directly. This
/// classifier reconstructs it from <c>PrintCte</c>'s own documented, stable prefixes rather than exposing
/// new engine surface area or re-running Resolve/Lower a second time just to reach the raw
/// <c>CteDefinition</c>. See <c>PlanExplainer.PrintCte</c>/<c>Print</c> (ignixa-fhir,
/// <c>Ignixa.Search.Sql/Ast/PlanExplainer.cs</c>) for the format this depends on.
/// </summary>
public static class PlanRowKindClassifier
{
    /// <summary>
    /// Classifies a plan row by its label first (a non-CTE label always means a result-shape modifier,
    /// regardless of body shape), then by its body's leading token for CTE rows ("root" or "cte{i}").
    /// Every <c>CteDefinition</c> case except <c>ParamSource</c> prints its own case name as the body's
    /// literal prefix (see <c>PrintCte</c>); <c>ParamSource</c> prints the physical table name instead
    /// (open-ended — new search-parameter tables can be added upstream), so it is the fallback for any
    /// cte-labelled row that doesn't match a known prefix, not a hard failure.
    /// </summary>
    public static string Classify(string label, string body)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentNullException.ThrowIfNull(body);

        if (label.StartsWith("inc", StringComparison.Ordinal))
        {
            return "Include";
        }

        switch (label)
        {
            case "sort":
                return "Sort";
            case "page":
                return "Page";
            case "countOnly":
                return "CountOnly";
        }

        // Every remaining label is a CTE row ("root" or "cte{i}").
        if (body.StartsWith("Intersect(", StringComparison.Ordinal))
        {
            return "Intersect";
        }

        if (body.StartsWith("Union(", StringComparison.Ordinal))
        {
            return "Union";
        }

        if (body.StartsWith("Except(", StringComparison.Ordinal))
        {
            return "Except";
        }

        if (body.StartsWith("ChainJoin(", StringComparison.Ordinal))
        {
            return "ChainJoin";
        }

        if (body.StartsWith("CompartmentSource[", StringComparison.Ordinal))
        {
            return "CompartmentSource";
        }

        if (body.StartsWith("ResourceSource[", StringComparison.Ordinal))
        {
            return "ResourceSource";
        }

        return "ParamSource";
    }
}
