using Aros.Api.Data;
using Aros.Api.Listening;
using Aros.Api.Sync;
using Aros.Api.Tts;
using Aros.Api.Vocab;
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

builder.Services.Configure<TtsOptions>(builder.Configuration.GetSection(TtsOptions.SectionName));
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<NarakeetClient>(client =>
{
    client.BaseAddress = new Uri("https://api.narakeet.com/");
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddScoped<TtsService>();
builder.Services.AddScoped<ListeningService>();
builder.Services.AddScoped<VocabService>();
builder.Services.AddScoped<VocabImporter>();

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
