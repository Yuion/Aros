using Aros.Api.Data;
using Aros.Api.Sync;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseWindowsService();

builder.Services.AddControllers();

var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(corsOrigins).AllowAnyHeader().AllowAnyMethod());
});

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// Auto-register all ISyncHandler implementations in this assembly
builder.Services.Scan(scan => scan
    .FromAssemblyOf<ISyncHandler>()
    .AddClasses(classes => classes.AssignableTo<ISyncHandler>())
    .AsImplementedInterfaces()
    .WithScopedLifetime());

var app = builder.Build();

app.UseCors();
app.MapControllers();

app.Run();
