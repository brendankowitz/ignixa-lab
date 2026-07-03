using System.Text.Json.Nodes;
using Ignixa.FhirFakes.Population;
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
