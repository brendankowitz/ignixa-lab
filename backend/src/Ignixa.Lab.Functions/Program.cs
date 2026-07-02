using Ignixa.Lab.Functions.Configuration;
using Ignixa.Lab.Functions.Execution;
using Ignixa.Lab.Functions.Http;
using Ignixa.Lab.Functions.Middleware;
using Ignixa.Lab.Functions.Suites;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = FunctionsApplication.CreateBuilder(args);

builder.ConfigureFunctionsWebApplication();

builder.UseMiddleware<CorsMiddleware>();
builder.UseMiddleware<RateLimitMiddleware>();

builder.Services
    .AddOptions<IgnixaLabOptions>()
    .Bind(builder.Configuration.GetSection(IgnixaLabOptions.SectionName));

builder.Services.AddSingleton<CorsMiddleware>();
builder.Services.AddSingleton<RateLimitPolicy>();
builder.Services.AddSingleton<RateLimitMiddleware>();

builder.Services.AddSingleton<IHttpExchangeScope, HttpExchangeScope>();
builder.Services.AddTransient<RecordingHttpHandler>();

builder.Services
    .AddHttpClient(HttpEvaluatorFactory.HttpClientName)
    .AddHttpMessageHandler<RecordingHttpHandler>();

builder.Services.AddSingleton<ISuiteCatalog, SuiteCatalog>();
builder.Services.AddSingleton<IEvaluatorFactory, HttpEvaluatorFactory>();
builder.Services.AddScoped<TestScriptRunner>();

builder.Services
    .AddApplicationInsightsTelemetryWorkerService()
    .ConfigureFunctionsApplicationInsights();

builder.Build().Run();
