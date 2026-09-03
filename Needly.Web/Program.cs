using Microsoft.AspNetCore.Authentication.Cookies;
using MudBlazor.Services;
using Needly.Infrastructure;
using Needly.Infrastructure.Actions;
using Needly.Infrastructure.GitHub;
using Needly.Web.Authentication;
using Needly.Web.Components;
using Needly.Web.Components.Views;
using Needly.Web.GitHub;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddMudServices();
builder.Services.AddNeedlyInfrastructure(
    builder.Configuration.GetConnectionString("Needly")
    ?? throw new InvalidOperationException("Connection string 'Needly' is required."));
builder.Services.AddNeedlyGitHubIntegration();
builder.Services.AddOptions<GitHubAppOptions>()
    .Bind(builder.Configuration.GetSection(GitHubAppOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<GitHubActionOptions>()
    .Bind(builder.Configuration.GetSection(GitHubActionOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddOptions<ActionRiskOptions>()
    .Bind(builder.Configuration.GetSection(ActionRiskOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddCascadingAuthenticationState();
builder.Services.AddScoped<SavedViewNavigationState>();
builder.Services.AddAuthorization();
var gitHubIntegrationEnabled = builder.Configuration
    .GetValue<bool>($"{GitHubAppOptions.SectionName}:Enabled");
var authentication = builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "Needly.Auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/auth/login";
        options.AccessDeniedPath = "/auth/denied";
    });
if (gitHubIntegrationEnabled)
{
    authentication.AddNeedlyGitHubOAuth(builder.Services);
    builder.Services.AddNeedlyGitHubWebhookProcessing();
}

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapStaticAssets();
app.MapNeedlyAuthenticationEndpoints(gitHubIntegrationEnabled);
if (gitHubIntegrationEnabled)
{
    app.MapNeedlyGitHubWebhookEndpoints();
}
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();