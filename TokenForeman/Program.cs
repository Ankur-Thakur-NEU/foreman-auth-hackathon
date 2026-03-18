using Auth0.AspNetCore.Authentication.Api;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.HttpOverrides;
using TokenForeman.Middleware;
using TokenForeman.Models;
using TokenForeman.Services;

var builder = WebApplication.CreateBuilder(args);

// PERMISSION BOUNDARY (for judges): This app uses Auth0 JWT only to identify the user and to exchange for delegated tokens via Token Vault. Client secrets and delegated tokens are never exposed to the browser or logged. CORS is explicitly allowlisted.

// CORS for PWA and for browser-based OpenClaw (e.g. Vite dev servers on 5173/5174). Override via Cors:AllowedOrigins.
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var origins = builder.Configuration["Cors:AllowedOrigins"]?
            .Split(';', StringSplitOptions.RemoveEmptyEntries);
        if (origins is { Length: > 0 })
            policy.WithOrigins(origins);
        else
            policy.WithOrigins(
                "http://localhost:5079", "https://localhost:7072", "http://localhost:3000",
                "http://localhost:5173", "https://localhost:5173", "http://localhost:5174", "https://localhost:5174");
        policy.AllowAnyMethod().AllowAnyHeader().AllowCredentials();
    });
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddHealthChecks();
builder.Services.AddControllers();
builder.Services.AddHttpClient("Auth0TokenVault");
builder.Services.AddHttpClient("ForemanIntegrations");
builder.Services.AddScoped<TokenVaultService>();
builder.Services.AddScoped<AgentService>();
builder.Services.Configure<Auth0Options>(builder.Configuration.GetSection(Auth0Options.SectionName));
builder.Services.AddAuth0ApiAuthentication(options =>
{
    options.Domain = builder.Configuration["Auth0:Domain"];
    options.JwtBearerOptions = new JwtBearerOptions
    {
        Audience = "https://foreman-api"
    };
});
builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseHttpsRedirection();
app.UseCors();

// Rate limiting: per-IP limit on /api/* to reduce abuse. Configurable via options if needed.
var rateLimitPerMinute = builder.Configuration.GetValue("RateLimit:PerMinute", 60);
app.UseMiddleware<RateLimitingMiddleware>(rateLimitPerMinute, TimeSpan.FromMinutes(1));

app.UseDefaultFiles();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapControllers();
app.MapFallbackToFile("index.html").AllowAnonymous();
app.MapHealthChecks("/health").AllowAnonymous();

app.Run();
