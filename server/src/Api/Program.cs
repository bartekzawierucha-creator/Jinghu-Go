using JianghuGo.Api;

if (args is ["migrate", ..])
{
    return await JianghuApi.MigrateAsync(args[1..]);
}

var app = JianghuApi.Create(args);
await app.RunAsync();
return 0;
