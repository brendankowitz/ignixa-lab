using FluentAssertions;
using Ignixa.Lab.Functions.Services.Search;

namespace Ignixa.Lab.Functions.Tests.Services.Search;

public sealed class PlanRowKindClassifierTests
{
    [Theory]
    // CTE rows (cte{i} or root) classify by their PlanExplainer.PrintCte body prefix.
    [InlineData("cte0", "StringSearchParam[1,1]  Text LIKE @p0 (StartsWith) collate CI_AI", "ParamSource")]
    [InlineData("root", "StringSearchParam[1,1]  Text LIKE @p0 (StartsWith) collate CI_AI", "ParamSource")]
    [InlineData("cte1", "TokenSearchParam[1,2]  Code = @p1", "ParamSource")]
    [InlineData("root", "Intersect(cte0, cte1)", "Intersect")]
    [InlineData("cte2", "Union(cte0, cte1)", "Union")]
    [InlineData("root", "Except(cte0, cte1)", "Except")]
    [InlineData("root", "ChainJoin(cte0, ref=1, inner=2, output=[1], Forward)", "ChainJoin")]
    [InlineData("cte3", "CompartmentSource[1,2,3],4  ReferenceResourceTypeId = @p0", "CompartmentSource")]
    [InlineData("cte0", "ResourceSource[1]", "ResourceSource")]
    // Non-CTE rows (result-shape modifiers) classify by their own label, regardless of body.
    [InlineData("inc0", "IncludeStage(ref=1, seedTypes=*, outputTypes=*, seeds=[match], limit=0, Forward)", "Include")]
    [InlineData("inc12", "IncludeStage(ref=1, seedTypes=*, outputTypes=*, seeds=[match], limit=0, Forward)", "Include")]
    [InlineData("sort", "SortSpec([String:1 ASC], Valued)", "Sort")]
    [InlineData("page", "PageSpec(boundary=[@p0], type=@p1, sid=@p2)", "Page")]
    [InlineData("countOnly", "true", "CountOnly")]
    public void Classify_ReturnsExpectedKind(string label, string body, string expectedKind)
    {
        PlanRowKindClassifier.Classify(label, body).Should().Be(expectedKind);
    }

    [Fact]
    public void Classify_UnrecognizedCteBodyShape_FallsBackToParamSource()
    {
        // ParamSource is the only CTE case with no distinguishing body prefix (it starts with the
        // physical table name, which is open-ended) -- an unrecognized cte-labelled body must fall
        // back to it rather than throw, since a table this classifier doesn't yet know about is still
        // a real ParamSource, not an error.
        PlanRowKindClassifier.Classify("cte4", "SomeFutureSearchParamTable[1,1]  Text = @p0").Should().Be("ParamSource");
    }
}
