using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.FhirFakes.Scenarios.States;
using Ignixa.FhirFakes.Workflow;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>Orchestrates calls into <c>Ignixa.FhirFakes</c> and shapes the results into plain JSON for the Fakes bench endpoints.</summary>
public sealed class FakesService(SchemaProviderFactory schemaProviderFactory)
{
    public JsonObject GeneratePopulation(string fhirVersion, string source, int count)
    {
        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        var generator = new PopulationGenerator(schemaProvider);

        var patients = new JsonArray();
        var resources = new JsonArray();
        var byType = new Dictionary<string, int>();
        var byGender = new Dictionary<string, int>();
        var byCity = new Dictionary<string, int>();
        var ageBuckets = new Dictionary<string, int> { ["0-17"] = 0, ["18-34"] = 0, ["35-54"] = 0, ["55-74"] = 0, ["75+"] = 0 };

        foreach (var context in generator.Generate(source, count))
        {
            if (context.Patient != null)
            {
                patients.Add(JsonNode.Parse(context.Patient.SerializeToString()));
                Tally(byGender, GetString(context.Patient, "gender") ?? "unknown");
                var city = GetAddressCity(context.Patient);
                if (city != null)
                {
                    Tally(byCity, city);
                }

                var age = GetAge(context.Patient);
                if (age != null)
                {
                    Tally(ageBuckets, AgeBucket(age.Value));
                }
            }

            foreach (var resource in context.AllResources)
            {
                resources.Add(JsonNode.Parse(resource.SerializeToString()));
                Tally(byType, resource.ResourceType);
            }
        }

        return new JsonObject
        {
            ["patients"] = patients,
            ["resources"] = resources,
            ["summary"] = new JsonObject
            {
                ["byType"] = ToJsonObject(byType),
                ["byGender"] = ToJsonObject(byGender),
                ["byCity"] = ToJsonObject(byCity),
                ["ageBuckets"] = ToJsonObject(ageBuckets),
            },
        };
    }

    /// <summary>Returns null when <paramref name="scenarioId"/> doesn't match a discovered scenario.</summary>
    public JsonObject? GenerateScenario(
        string fhirVersion,
        string scenarioId,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        string? tag,
        bool resolvedReferences)
    {
        var scenario = ScenarioCatalog.Find(scenarioId);
        if (scenario is null)
        {
            return null;
        }

        var overrides = ConvertParameterOverrides(scenario, parameters);

        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        ScenarioContext context;
        try
        {
            context = ScenarioCatalog.Invoke(scenario, schemaProvider, overrides);
        }
        catch (ScenarioInvocationException ex)
        {
            throw new InvalidScenarioParametersException($"Invalid scenario parameters: {ex.Message}", ex);
        }

        if (resolvedReferences)
        {
            context.RewriteReferences(schemaProvider.ReferenceMetadataProvider, ReferenceFormat.Resolved);
        }

        var bundle = resolvedReferences ? context.ToBatchBundle() : context.ToBundle();

        var patientNode = context.Patient != null ? JsonNode.Parse(context.Patient.SerializeToString()) : null;
        var resourceNodes = context.AllResources.Select(r => JsonNode.Parse(r.SerializeToString())!).ToList();
        var bundleNode = JsonNode.Parse(bundle.SerializeToString())!.AsObject();

        if (!string.IsNullOrWhiteSpace(tag))
        {
            if (patientNode != null)
            {
                StampTag(patientNode.AsObject(), tag);
            }

            foreach (var resourceNode in resourceNodes)
            {
                StampTag(resourceNode.AsObject(), tag);
            }

            StampBundleEntryTags(bundleNode, tag);
        }

        return new JsonObject
        {
            ["patient"] = patientNode,
            ["resources"] = new JsonArray(resourceNodes.ToArray()),
            ["bundle"] = bundleNode,
        };
    }

    /// <summary>Returns null when <paramref name="packId"/> doesn't match a discovered workflow scenario pack.</summary>
    public JsonObject? GenerateWorkflow(
        string fhirVersion,
        string packId,
        IReadOnlyDictionary<string, JsonElement>? parameters,
        int? seed,
        string? tag,
        bool resolvedReferences)
    {
        var pack = WorkflowScenarioCatalog.Find(packId);
        if (pack is null)
        {
            return null;
        }

        var overrides = ConvertParameterOverrides(pack, parameters);

        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        var options = new WorkflowScenarioOptions { Seed = seed, Tag = tag };

        WorkflowScenarioResult result;
        try
        {
            result = WorkflowScenarioCatalog.Invoke(pack, schemaProvider, options, overrides);
        }
        catch (ScenarioInvocationException ex)
        {
            throw new InvalidScenarioParametersException($"Invalid scenario parameters: {ex.Message}", ex);
        }

        var resources = result.Graph.AllResources;
        var bundle = resolvedReferences
            ? ResourceBundleComposer.ToBatchBundle(resources)
            : ResourceBundleComposer.ToTransactionBundle(resources);

        var resourceNodes = resources.Select(r => JsonNode.Parse(r.SerializeToString())!).ToList();
        var bundleNode = JsonNode.Parse(bundle.SerializeToString())!.AsObject();

        if (!string.IsNullOrWhiteSpace(tag))
        {
            foreach (var resourceNode in resourceNodes)
            {
                StampTag(resourceNode.AsObject(), tag);
            }

            StampBundleEntryTags(bundleNode, tag);
        }

        return new JsonObject
        {
            ["resources"] = new JsonArray(resourceNodes.ToArray()),
            ["bundle"] = bundleNode,
            ["resourceCountsByType"] = ToJsonObject(result.Manifest.ResourceCountsByType),
        };
    }

    public JsonObject GenerateResource(
        string fhirVersion,
        string resourceType,
        int seed,
        string density,
        string? theme,
        string? firstName,
        string? familyName,
        string? city,
        string? observationState,
        IReadOnlyList<string>? edgeCaseSelectors,
        bool includeInvalid)
    {
        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        var generationDensity = Enum.TryParse<GenerationDensity>(density, ignoreCase: true, out var parsedDensity)
            ? parsedDensity
            : GenerationDensity.Minimal;
        ClinicalDomain? clinicalTheme = !string.IsNullOrWhiteSpace(theme) && Enum.TryParse<ClinicalDomain>(theme, ignoreCase: true, out var parsedTheme)
            ? parsedTheme
            : null;

        var resource = BuildResource(schemaProvider, resourceType, seed, generationDensity, clinicalTheme, firstName, familyName, city, observationState);

        JsonObject? manifestJson = null;
        if (edgeCaseSelectors is { Count: > 0 })
        {
            var catalog = EdgeCaseCatalog.CreateDefault();
            var strategies = catalog.Resolve(edgeCaseSelectors, out var unmatched);
            if (unmatched.Count > 0)
            {
                throw new UnknownEdgeCaseSelectorsException(
                    $"Unknown edge case selector(s): {string.Join(", ", unmatched)}")
                {
                    Unmatched = unmatched,
                };
            }

            var pipeline = new EdgeCasePipeline(seed, schemaProvider);
            var manifest = pipeline.Apply(resource, strategies, includeInvalid);
            manifestJson = new JsonObject
            {
                ["resourceId"] = manifest.ResourceId,
                ["seed"] = manifest.Seed,
                ["mutations"] = new JsonArray(manifest.Mutations.Select(m => (JsonNode)new JsonObject
                {
                    ["category"] = m.Category,
                    ["path"] = m.Path,
                    ["before"] = m.Before,
                    ["after"] = m.After,
                    ["description"] = m.Description,
                }).ToArray()),
            };
        }

        return new JsonObject
        {
            ["resource"] = JsonNode.Parse(resource.SerializeToString()),
            ["manifest"] = manifestJson,
        };
    }

    /// <summary>
    /// Converts the request's raw JSON parameter overrides into the strongly-typed values
    /// <see cref="ScenarioCatalog.Invoke"/> expects, using each parameter's own
    /// <see cref="DiscoveredScenarioParameter.TryParseValue(string, out object?, out string?)"/> —
    /// which already applies invariant-culture parsing and the scenario's declared Min/Max range,
    /// so this method no longer needs its own type-conversion switch. An override key with no
    /// matching parameter is ignored here and left for <see cref="ScenarioCatalog.Invoke"/>'s own
    /// binder to silently skip, matching this method's prior behavior of not pre-validating names.
    /// </summary>
    private static IReadOnlyDictionary<string, object?>? ConvertParameterOverrides(
        DiscoveredScenario scenario,
        IReadOnlyDictionary<string, JsonElement>? parameters)
    {
        if (parameters is null || parameters.Count == 0)
        {
            return null;
        }

        var parameterMetadata = scenario.Parameters.ToDictionary(p => p.Name, StringComparer.OrdinalIgnoreCase);
        var overrides = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

        foreach (var (key, jsonValue) in parameters)
        {
            if (!parameterMetadata.TryGetValue(key, out var parameter))
            {
                continue;
            }

            if (jsonValue.ValueKind == JsonValueKind.Null)
            {
                overrides[parameter.Name] = null;
                continue;
            }

            var rawValue = jsonValue.ValueKind == JsonValueKind.String ? jsonValue.GetString()! : jsonValue.GetRawText();
            if (!parameter.TryParseValue(rawValue, out var value, out var failureReason))
            {
                throw new InvalidScenarioParametersException(
                    $"Invalid scenario parameters: parameter '{parameter.Name}' {failureReason ?? $"could not be converted from '{rawValue}'."}");
            }

            overrides[parameter.Name] = value;
        }

        return overrides;
    }

    /// <summary>
    /// Patient uses <see cref="PatientBuilderFactory"/> directly; Observation with a
    /// requested clinical state goes through
    /// <see cref="ScenarioBuilder"/>.AddObservation(ObservationState) — the real,
    /// public entry point that consumes an <c>ObservationState</c> (there is no
    /// direct <c>ObservationBuilder.FromState(...)</c> — <c>ObservationBuilder</c>
    /// has its own separate fluent API unrelated to <c>ObservationState</c>).
    /// <c>WithPatient()</c> must run first: <c>ObservationState.Execute</c> throws
    /// "Cannot create Observation without a Patient" if the scenario has no
    /// initial Patient state, so we seed a default one and then extract the
    /// resulting <see cref="ScenarioContext"/>'s Observation from
    /// <c>AllResources</c>. Everything else uses the generic schema-driven faker.
    /// </summary>
    private static Ignixa.Serialization.SourceNodes.ResourceJsonNode BuildResource(
        Ignixa.Abstractions.IFhirSchemaProvider schemaProvider,
        string resourceType,
        int seed,
        GenerationDensity density,
        ClinicalDomain? theme,
        string? firstName,
        string? familyName,
        string? city,
        string? observationState)
    {
        if (string.Equals(resourceType, "Patient", StringComparison.OrdinalIgnoreCase))
        {
            var builder = PatientBuilderFactory.Create(schemaProvider, seed);

            // .WithCity(string) only sets the address line's city text — it does not sample
            // gender/age/zip/area code the way .FromCity(CityDemographics) does, so a plain
            // .WithCity() call left gender unset and every generated Patient reported "unknown".
            // Route through FromCity() for any of the known demographics cities so gender (and
            // the rest of the profile) gets realistically sampled from the seeded RNG; an
            // unrecognized city name (e.g. from a direct API caller) falls back to the old
            // text-only behavior rather than being silently dropped.
            var cityDemographics = !string.IsNullOrWhiteSpace(city)
                ? KnownCities.All.FirstOrDefault(known => string.Equals(known.Name, city, StringComparison.OrdinalIgnoreCase))
                : null;

            if (cityDemographics != null)
            {
                builder = builder.FromCity(cityDemographics);
            }
            else if (!string.IsNullOrWhiteSpace(city))
            {
                builder = builder.WithCity(city);
            }

            if (!string.IsNullOrWhiteSpace(firstName))
            {
                builder = builder.WithGivenName(firstName);
            }

            if (!string.IsNullOrWhiteSpace(familyName))
            {
                builder = builder.WithFamilyName(familyName);
            }

            return builder.Build();
        }

        if (string.Equals(resourceType, "Observation", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(observationState))
        {
            if (!ObservationStateCatalog.TryCreate(observationState, out var state))
            {
                throw new InvalidOperationException(
                    $"Observation state '{observationState}' passed validation but ObservationStateCatalog.TryCreate returned false.");
            }

            var context = new ScenarioBuilder(schemaProvider).WithPatient().AddObservation(state).Build();
            var observation = context.AllResources.FirstOrDefault(r => r.ResourceType == "Observation")
                ?? throw new InvalidOperationException(
                    $"Observation state '{observationState}' produced a scenario with no Observation resource.");

            return observation;
        }

        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed) { Density = density, Theme = theme };
        return faker.Generate(resourceType);
    }

    private static void StampTag(JsonObject resource, string tag)
    {
        var meta = resource["meta"]?.AsObject() ?? new JsonObject();
        var tags = meta["tag"]?.AsArray() ?? new JsonArray();
        tags.Add(new JsonObject { ["system"] = "urn:ignixa:test", ["code"] = tag });
        meta["tag"] = tags;
        resource["meta"] = meta;
    }

    private static void StampBundleEntryTags(JsonObject bundle, string tag)
    {
        if (bundle["entry"] is not JsonArray entries)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (entry?["resource"] is JsonObject resource)
            {
                StampTag(resource, tag);
            }
        }
    }

    private static void Tally(Dictionary<string, int> counts, string key) =>
        counts[key] = counts.GetValueOrDefault(key) + 1;

    private static JsonObject ToJsonObject(IReadOnlyDictionary<string, int> counts)
    {
        var obj = new JsonObject();
        foreach (var (key, value) in counts)
        {
            obj[key] = value;
        }

        return obj;
    }

    private static string? GetString(Ignixa.Serialization.SourceNodes.ResourceJsonNode resource, string propertyName)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(resource.SerializeToString());
        return doc.RootElement.TryGetProperty(propertyName, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string? GetAddressCity(Ignixa.Serialization.SourceNodes.ResourceJsonNode resource)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(resource.SerializeToString());
        if (!doc.RootElement.TryGetProperty("address", out var address) || address.GetArrayLength() == 0)
        {
            return null;
        }

        var first = address[0];
        return first.TryGetProperty("city", out var city) ? city.GetString() : null;
    }

    private static int? GetAge(Ignixa.Serialization.SourceNodes.ResourceJsonNode resource)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(resource.SerializeToString());
        if (!doc.RootElement.TryGetProperty("birthDate", out var birthDate) || birthDate.GetString() is not { } text)
        {
            return null;
        }

        return DateTime.TryParse(text[..4] + "-01-01", out var parsed) ? DateTime.UtcNow.Year - parsed.Year : null;
    }

    private static string AgeBucket(int age) => age switch
    {
        < 18 => "0-17",
        < 35 => "18-34",
        < 55 => "35-54",
        < 75 => "55-74",
        _ => "75+",
    };
}
