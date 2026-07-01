using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Lab.Functions.Suites;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.Services
    .AddOptions<IgnixaLabOptions>()
    .Bind(builder.Configuration.GetSection(IgnixaLabOptions.SectionName));

builder.Services.AddHttpClient(HttpEvaluatorFactory.HttpClientName);

builder.Services.AddSingleton<ISuiteCatalog, SuiteCatalog>();
builder.Services.AddSingleton<IEvaluatorFactory, HttpEvaluatorFactory>();
builder.Services.AddScoped<TestScriptRunner>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
