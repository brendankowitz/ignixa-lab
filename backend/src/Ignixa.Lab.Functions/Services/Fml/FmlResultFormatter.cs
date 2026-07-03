using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Nodes;
using Ignixa.FhirMappingLanguage.Evaluation;
using Ignixa.Lab.Functions.Models;
using Ignixa.Serialization;
using Ignixa.Serialization.Models;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Services.Fml;

/// <summary>
/// Formats an <see cref="FmlResult"/> as a FHIR Parameters resource matching
/// the shape fhirpath-lab.com's FML UI already parses: "parameters"/"trace"/
/// "result"/"outcome" parts.
/// </summary>
public sealed class FmlResultFormatter
{
    private const string EvaluatorName = "Ignixa .NET (FML)";

    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Instance method by design so it can be consumed via DI and mocked in tests.")]
    public ResourceJsonNode FormatResult(FmlResult result, bool debug)
    {
        if (!result.IsSuccess)
        {
            return FhirPath.ResultFormatter.CreateOperationOutcomeResult("error", "invalid", result.Error!, result.Error);
        }

        var parameters = new ParametersJsonNode();

        var configParam = new ParameterJsonNode { Name = "parameters" };
        parameters.Parameter.Add(configParam);
        AddPart(configParam, "evaluator", EvaluatorName);
        AddPart(configParam, "map", result.Request.Map);

        foreach (var logLine in result.LogLines)
        {
            AddTracePart(parameters, logLine);
        }

        if (debug)
        {
            foreach (var error in result.Errors)
            {
                AddTracePart(parameters, error.ToString() ?? error.Message);
            }
        }

        var resultParam = new ParameterJsonNode { Name = "result" };
        resultParam.SetValue("valueString", result.Output!.SerializeToString(pretty: true));
        parameters.Parameter.Add(resultParam);

        var outcomeParam = new ParameterJsonNode { Name = "outcome" };
        var outcome = BuildOutcome(result.Errors);
        outcomeParam.MutableNode["resource"] = JsonNode.Parse(outcome.SerializeToString());
        parameters.Parameter.Add(outcomeParam);

        return parameters;
    }

    private static void AddTracePart(ParametersJsonNode parameters, string message)
    {
        var part = new ParameterJsonNode { Name = "trace" };
        part.SetValue("valueString", message);
        parameters.Parameter.Add(part);
    }

    private static OperationOutcomeJsonNode BuildOutcome(IReadOnlyList<ExecutionError> errors)
    {
        var outcome = new OperationOutcomeJsonNode();

        if (errors.Count == 0)
        {
            outcome.Issue.Add(new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Information,
                Code = OperationOutcomeJsonNode.IssueType.Informational,
                Details = new CodeableConceptJsonNode { Text = "Transformation completed successfully" }
            });
            return outcome;
        }

        foreach (var error in errors)
        {
            var issue = new OperationOutcomeJsonNode.IssueComponent
            {
                Severity = OperationOutcomeJsonNode.IssueSeverity.Error,
                Code = OperationOutcomeJsonNode.IssueType.Exception,
                Details = new CodeableConceptJsonNode { Text = error.Message },
                Diagnostics = error.ToString() ?? error.Message
            };
            if (!string.IsNullOrEmpty(error.ElementPath))
            {
                issue.Expression.Add(error.ElementPath);
            }
            outcome.Issue.Add(issue);
        }

        return outcome;
    }

    private static void AddPart(ParameterJsonNode parent, string name, string value)
    {
        var part = new ParameterJsonNode { Name = name };
        part.SetValue("valueString", value);
        parent.Part.Add(part);
    }
}
