using JianghuGo.Composition;
using JianghuGo.Infrastructure;
using JianghuGo.Infrastructure.Outbox;

var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Logging.ClearProviders();
builder.Logging.AddJsonConsole(o =>
{
    o.IncludeScopes = true;
    o.UseUtcTimestamp = true;
});

if (StorageProvider.From(builder.Configuration) != StorageKind.Postgres)
{
    // An in-memory outbox lives inside the API process; a separate worker could never see it.
    throw new InvalidOperationException("JianghuGo.Worker requires Storage:Provider=Postgres. For in-memory development set Worker:RunInProcess=true in the API.");
}

builder.Services.AddJianghuCore(builder.Configuration, builder.Environment);
builder.Services.AddHostedService(sp => sp.GetRequiredService<OutboxDispatcher>());
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(20));

await builder.Build().RunAsync();
