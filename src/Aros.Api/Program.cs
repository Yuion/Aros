using Aros.Api.Data;
using Aros.Api.Listening;
using Aros.Api.Sync;
using Aros.Api.Tts;
using Aros.Api.Tutor;
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
builder.Services.Configure<AiOptions>(builder.Configuration.GetSection(AiOptions.SectionName));
builder.Services.AddMemoryCache();

builder.Services.AddHttpClient<NarakeetClient>(client =>
{
    client.BaseAddress = new Uri("https://api.narakeet.com/");
    client.Timeout = TimeSpan.FromSeconds(60);
});

builder.Services.AddHttpClient<OpenAiClient>((provider, client) =>
{
    var ai = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<AiOptions>>().Value;
    client.BaseAddress = new Uri(ai.BaseUrl);
    client.Timeout = TimeSpan.FromMinutes(5);        // a long lesson answer is a long generation
});

builder.Services.AddScoped<TtsService>();
builder.Services.AddScoped<ListeningService>();
builder.Services.AddScoped<VocabService>();
builder.Services.AddScoped<VocabImporter>();
builder.Services.AddScoped<CourseState>();
builder.Services.AddScoped<AiBudget>();
builder.Services.AddScoped<TutorService>();
builder.Services.AddScoped<CourseImporter>();
builder.Services.AddScoped<LessonRecorder>();

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
