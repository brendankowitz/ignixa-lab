using System.Text.Json;
using System.Text.Json.Nodes;
using Ignixa.Abstractions;
using Ignixa.FhirFakes;
using Ignixa.FhirFakes.Builders;
using Ignixa.FhirFakes.EdgeCases;
using Ignixa.FhirFakes.Population;
using Ignixa.FhirFakes.Scenarios;
using Ignixa.Lab.Functions.Services.FhirPath;
using Ignixa.Serialization;
using Ignixa.Serialization.SourceNodes;

namespace Ignixa.Lab.Functions.Services.Fakes;

/// <summary>Orchestrates calls into <c>Ignixa.FhirFakes</c> and shapes the results into plain JSON for the Fakes bench endpoints.</summary>
public sealed class FakesService(
    SchemaProviderFactory schemaProviderFactory,
    ScenarioDiscovery scenarioDiscovery,
    ObservationStateDiscovery observationStateDiscovery)
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
        var scenario = scenarioDiscovery.Find(scenarioId);
        if (scenario is null)
        {
            return null;
        }

        var schemaProvider = schemaProviderFactory.GetSchemaProvider(fhirVersion);
        var context = scenarioDiscovery.Invoke(scenario, schemaProvider, parameters);

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

    public JsonObject GenerateResource(
        string fhirVersion,
        string resourceType,
        int seed,
        string density,
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

        var resource = BuildResource(schemaProvider, resourceType, seed, generationDensity, firstName, familyName, city, observationState);

        JsonObject? manifestJson = null;
        if (edgeCaseSelectors is { Count: > 0 })
        {
            var catalog = EdgeCaseCatalog.CreateDefault();
            var strategies = catalog.Resolve(edgeCaseSelectors, out _);
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
    /// Instance method (not static) because the Observation-with-state path needs
    /// <c>observationStateDiscovery</c>. Patient uses <see cref="PatientBuilderFactory"/>
    /// directly; Observation with a requested clinical state goes through
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
    private Ignixa.Serialization.SourceNodes.ResourceJsonNode BuildResource(
        Ignixa.Abstractions.IFhirSchemaProvider schemaProvider,
        string resourceType,
        int seed,
        GenerationDensity density,
        string? firstName,
        string? familyName,
        string? city,
        string? observationState)
    {
        if (string.Equals(resourceType, "Patient", StringComparison.OrdinalIgnoreCase))
        {
            var builder = PatientBuilderFactory.Create(schemaProvider, seed);
            if (!string.IsNullOrWhiteSpace(firstName))
            {
                builder = builder.WithGivenName(firstName);
            }

            if (!string.IsNullOrWhiteSpace(familyName))
            {
                builder = builder.WithFamilyName(familyName);
            }

            if (!string.IsNullOrWhiteSpace(city))
            {
                builder = builder.WithCity(city);
            }

            return builder.Build();
        }

        if (string.Equals(resourceType, "Observation", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(observationState))
        {
            var state = observationStateDiscovery.Create(observationState);
            if (state != null)
            {
                var context = new ScenarioBuilder(schemaProvider).WithPatient().AddObservation(state).Build();
                var observation = context.AllResources.FirstOrDefault(r => r.ResourceType == "Observation");
                if (observation != null)
                {
                    return observation;
                }
            }
        }

        var faker = new SchemaBasedFhirResourceFaker(schemaProvider, seed) { Density = density };
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

    private static JsonObject ToJsonObject(Dictionary<string, int> counts)
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
