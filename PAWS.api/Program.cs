using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using PAWS.api.ErrorHandling;
using PAWS.api.Services;
using PAWS.domain.Constants;
using PAWS.infrastructure.Data;
using PAWS.infrastructure.Services;
using System.Text;

// Stop ASP.NET Core from silently renaming standard JWT claims (like "sub") to
// long legacy URIs. Without this, GetCurrentUserId() (UsersController) would not
// find the "sub" claim by its real name.
Microsoft.IdentityModel.JsonWebTokens.JsonWebTokenHandler.DefaultInboundClaimTypeMap.Clear();

var builder = WebApplication.CreateBuilder(args);

// MASTER database: the tenant registry (Companies, CompanyDatabases, Identity).
builder.Services.AddDbContext<MasterErpDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("MasterErp")));

// Finds which tenant database belongs to a company.
builder.Services.AddScoped<ITenantDatabaseResolver, TenantDatabaseResolver>();

// Builds a TenantErpDbContext connected to that company's database.
builder.Services.AddScoped<ITenantDbContextFactory, TenantDbContextFactory>();

// The ONE place that decides which company the current request belongs to.
// From this step onward, it reads ONLY the signed-in user's token.
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentCompanyService, CurrentCompanyService>();

// Hashes and checks passwords. Passwords are NEVER stored or compared as plain text.
builder.Services.AddSingleton<IPasswordService, PasswordService>();

// Sign-in tokens. Issuer, audience and lifetime have defaults in JwtSettings.
// The SigningKey comes ONLY from User Secrets ("Jwt:SigningKey").
// If it is missing or too short, the API refuses to start instead of running unsafely.
builder.Services
    .AddOptions<JwtSettings>()
    .Bind(builder.Configuration.GetSection(JwtSettings.SectionName))
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.SigningKey) && settings.SigningKey.Length >= 32,
        "Jwt:SigningKey is missing or shorter than 32 characters. Set it in User Secrets.")
    .ValidateOnStart();
builder.Services.AddSingleton<ITokenService, TokenService>();

// The setup key that guards creation of the FIRST Administrator of a company.
// Comes ONLY from User Secrets ("Bootstrap:SetupKey"). The API refuses to start
// if it is missing or too short, exactly like the JWT signing key above.
builder.Services
    .AddOptions<BootstrapSettings>()
    .Bind(builder.Configuration.GetSection(BootstrapSettings.SectionName))
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.SetupKey) && settings.SetupKey.Length >= 16,
        "Bootstrap:SetupKey is missing or shorter than 16 characters. Set it in User Secrets.")
    .ValidateOnStart();

// The setup key that guards creation of the FIRST Super Admin (a master-database
// Identity account). Separate from Bootstrap:SetupKey (tenant Administrators).
builder.Services
    .AddOptions<SuperAdminBootstrapSettings>()
    .Bind(builder.Configuration.GetSection(SuperAdminBootstrapSettings.SectionName))
    .Validate(
        settings => !string.IsNullOrWhiteSpace(settings.SetupKey) && settings.SetupKey.Length >= 16,
        "SuperAdminBootstrap:SetupKey is missing or shorter than 16 characters. Set it in User Secrets.")
    .ValidateOnStart();

// Identity services for the Super Admin (master-database) account system.
// AddIdentityCore is the lightweight version: it gives us UserManager/RoleManager
// without pulling in cookie-based web login, since we use our own JWT tokens.
builder.Services
    .AddIdentityCore<IdentityUser>()
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<MasterErpDbContext>();

// ----- NEW in Step 15.4: verify tokens on incoming requests -----
// Read the same non-secret settings and the same secret key used to CREATE tokens
// (Step 15.2), so the API can check a token's signature and expiry.
var jwtSection = builder.Configuration.GetSection(JwtSettings.SectionName);
var jwtIssuer = jwtSection["Issuer"] ?? "PAWS.api";
var jwtAudience = jwtSection["Audience"] ?? "PAWS.winui";
var jwtSigningKey = jwtSection["SigningKey"] ?? string.Empty;

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,

            ValidateAudience = true,
            ValidAudience = jwtAudience,

            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSigningKey)),

            ValidateLifetime = true,

            // Tell ASP.NET Core exactly which claim in OUR tokens (Step 15.2's
            // PawsClaims) represents the user's name and role, instead of relying
            // on any automatic remapping. This makes [Authorize(Roles = "...")]
            // and User.Identity.Name work correctly with our own claim names.
            NameClaimType = PawsClaims.Name,
            RoleClaimType = PawsClaims.Role
        };
    });

builder.Services.AddAuthorization();
// ----- END NEW -----

// Central error handling: turns crashes into short, safe error responses.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Add services to the container.

builder.Services.AddControllers();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Must come FIRST so it can catch errors from everything below it.
app.UseExceptionHandler();

// Gives empty error responses (like 404 Not Found) the same safe JSON format.
app.UseStatusCodePages();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

// Check WHO is calling (authentication), then WHAT they may do (authorization).
// Authentication must run before Authorization, and both before MapControllers().
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();