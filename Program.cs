using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MyBGList.Constants;
using MyBGList.Models;
using MyBGList.Swagger;
using Serilog;
using Serilog.Sinks.MSSqlServer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authorization;
using MyBGList.GraphQL;
using MyBGList.gRPC;
using System.Reflection;
using Swashbuckle.AspNetCore.Annotations;
using MyBGList.Attributes;

var builder = WebApplication.CreateBuilder(args);

#region Logging

builder.Logging
    .ClearProviders()
    .AddJsonConsole(options =>
    {
        options.TimestampFormat = "HH:mm";
        options.UseUtcTimestamp = true;
    })
    .AddDebug();
    //.AddApplicationInsights(telemetry =>
    //    telemetry.ConnectionString = builder.Configuration["Azure:ApplicationInsights:ConnectionString"],
    //    loggerOptions => { });

builder.Host.UseSerilog((ctx, lc) =>
{
    lc.ReadFrom.Configuration(ctx.Configuration);
    lc.Enrich.WithMachineName();
    lc.Enrich.WithThreadId();
    lc.Enrich.WithThreadName();
    lc.WriteTo.File("Logs/log.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] " + "[{MachineName}#{ThreadId} {ThreadName}] " +
        "{Message:lj}{NewLine}{Exception}");
    lc.WriteTo.File("Logs/errors.txt",
        rollingInterval: RollingInterval.Day,
        outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] " + "[{MachineName}#{ThreadId} {ThreadName}] " +
        "{Message:lj}{NewLine}{Exception}",
        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Error);
    lc.WriteTo.MSSqlServer(connectionString:
        ctx.Configuration.GetConnectionString("DefaultConnection"),
        sinkOptions: new MSSqlServerSinkOptions
        {
            TableName = "LogEvents",
            AutoCreateSqlTable = true
        },
        columnOptions: new ColumnOptions()
        {
            AdditionalColumns = new SqlColumn[]
            {
                new SqlColumn()
                {
                    ColumnName = "SourceContext",
                    PropertyName = "SourceContext",
                    DataType = System.Data.SqlDbType.NVarChar
                }
            }
        }); 
},
writeToProviders: true);

#endregion

// Add services to the container.

builder.Services.AddControllers(options =>
{
    options.ModelBindingMessageProvider.SetValueIsInvalidAccessor(
        (x) => $"The value '{x}' is invalid.");
    options.ModelBindingMessageProvider.SetValueMustBeANumberAccessor(
        (x) => $"The field {x} must be a number.");
    options.ModelBindingMessageProvider.SetMissingKeyOrValueAccessor(
        () => $"A value is required");
    options.CacheProfiles.Add("NoCache",
        new CacheProfile() { NoStore = true });
    options.CacheProfiles.Add("Any-60",
        new CacheProfile() { Location = ResponseCacheLocation.Any, Duration = 60 });
    options.CacheProfiles.Add("Client-120",
        new CacheProfile() { Location = ResponseCacheLocation.Client, Duration = 120 });
});
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.ParameterFilter<SortColumnFilter>();
    options.ParameterFilter<SortOrderFilter>();
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        In = ParameterLocation.Header,
        Description = "Please enter token",
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        BearerFormat = "JWT",
        Scheme = "bearer"
    });
    //options.AddSecurityRequirement(new OpenApiSecurityRequirement
    //{
    //    {
    //        new OpenApiSecurityScheme
    //        {
    //            Reference = new OpenApiReference
    //            {
    //                Type = ReferenceType.SecurityScheme,
    //                Id = "Bearer"
    //            }
    //        },
    //        Array.Empty<string>()
    //    }
    //});
    
    var xmlFilename = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    options.IncludeXmlComments(System.IO.Path.Combine(
        AppContext.BaseDirectory, xmlFilename));
    options.EnableAnnotations();
    options.OperationFilter<AuthRequirementFilter>();
    options.DocumentFilter<CustomDocumentFilter>();
    options.RequestBodyFilter<PasswordRequestFilter>();
    options.SchemaFilter<CustomKeyValueFilter>();
    options.RequestBodyFilter<UsernameRequestFilter>();
});
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(cfg =>
    {
        cfg.WithOrigins(builder.Configuration["AllowedOrigins"]);
        cfg.AllowAnyHeader();
        cfg.AllowAnyMethod();
    });
    options.AddPolicy(name: "AnyOrigin", cfg =>
    {
        cfg.AllowAnyOrigin();
        cfg.AllowAnyHeader();
        cfg.AllowAnyMethod();
    });
});
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"))
    );

builder.Services.AddGraphQLServer()
    .AddAuthorization()
    .AddQueryType<Query>()
    .AddMutationType<Mutation>()
    .AddProjections()
    .AddFiltering()
    .AddSorting();

builder.Services.AddGrpc();

builder.Services.AddIdentity<ApiUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = true;
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = true;
    options.Password.RequiredLength = 12;
}).AddEntityFrameworkStores<ApplicationDbContext>();

builder.Services.AddResponseCaching(options =>
{
    options.MaximumBodySize = 128 * 1024 * 1024;
    options.SizeLimit = 200 * 1024 * 1024;
    options.UseCaseSensitivePaths = true;
});

builder.Services.AddMemoryCache();

//builder.Services.AddDistributedSqlServerCache(options =>
//{
//    options.ConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
//    options.SchemaName = "dbo";
//    options.TableName = "AppCache";
//});

builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"];
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme =
    options.DefaultChallengeScheme =
    options.DefaultForbidScheme =
    options.DefaultScheme =
    options.DefaultSignInScheme =
    options.DefaultSignOutScheme =
    JwtBearerDefaults.AuthenticationScheme;
}).AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = builder.Configuration["JWT:Issuer"],
        ValidateAudience = true,
        ValidAudience = builder.Configuration["JWT:Audience"],
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(
            System.Text.Encoding.UTF8.GetBytes(
                builder.Configuration["JWT:SigningKey"]))
    };
});

//builder.Services.AddAuthorization(options =>
//{
//    options.AddPolicy("ModeratorWithMobilePhone", policy =>
//    policy.RequireClaim(ClaimTypes.Role, RoleNames.Moderator)
//    .RequireClaim(ClaimTypes.MobilePhone));
//});

//builder.Services.Configure<ApiBehaviorOptions>(options =>
//    options.SuppressModelStateInvalidFilter = true);
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
} else
{
    app.UseHsts();
    app.Use(async (context, next) =>
    {
        context.Response.Headers.Add("X-Frame-Options",
            "sameorigin");
        context.Response.Headers.Add("X-XSS-Protection",
            "1; mode=block");
        context.Response.Headers.Add("X-Context-Type-Options",
            "nosniff");
        context.Response.Headers.Add("Context-Security-Policy",
            "default-src ' self' ; script-src 'self' 'nonce-23a98b38c'");
        context.Response.Headers.Add("Referrer-Policy",
            "strict-origin");
        await next();
    });
}


if (app.Configuration.GetValue<bool>("UseDeveloperExceptionPage"))
    app.UseDeveloperExceptionPage();
else
    app.UseExceptionHandler("/error");

app.UseHttpsRedirection();

app.UseCors();

app.UseResponseCaching();

app.UseAuthentication();

app.UseAuthorization();

app.MapGraphQL();

app.MapGrpcService<GrpcService>();

app.Use((context, next) =>
{
    context.Response.GetTypedHeaders().CacheControl =
        new Microsoft.Net.Http.Headers.CacheControlHeaderValue()
        {
            NoCache = true,
            NoStore = true
        };
    return next.Invoke();
});

#region Minimal API

app.MapGet("/error",
[EnableCors("AnyOrigins")]
[ResponseCache(CacheProfileName = "NoCache")]
(HttpContext context) =>
    {
        var exceptionHandler = context.Features.Get<IExceptionHandlerPathFeature>();
        var details = new ProblemDetails();
        details.Detail = exceptionHandler?.Error.Message;
        details.Extensions["traceId"] = System.Diagnostics.Activity.Current?.Id
            ?? context.TraceIdentifier;
        if (exceptionHandler?.Error is NotImplementedException)
        {
            details.Type = "https://tools.ietf.org/html/rfc7231#section-6.6.2";
            details.Status = StatusCodes.Status501NotImplemented;
        }
        else if (exceptionHandler?.Error is TimeoutException)
        {
            details.Type = "https://tools.ietf.org/html/rfc7231#section-6.6.5";
            details.Status = StatusCodes.Status504GatewayTimeout;
        }
        else
        {
            details.Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1";
            details.Status = StatusCodes.Status500InternalServerError;
        }
        app.Logger.LogError(CustomLogEvents.Error_Get, 
            exceptionHandler?.Error, 
            "An unhandled exception occurred." + "{errorMessage}", 
            exceptionHandler?.Error.Message);
        return Results.Problem(details);
    });

app.MapGet("/error/test", [EnableCors("AnyOrigins")] [ResponseCache(CacheProfileName = "NoCache")] () => { throw new Exception("test"); });
app.MapGet("/error/test/501", [EnableCors("AnyOrigins")][ResponseCache(CacheProfileName = "NoCache")] () => { throw new NotImplementedException("test 501"); });
app.MapGet("/error/test/504", [EnableCors("AnyOrigins")][ResponseCache(CacheProfileName = "NoCache")] () => { throw new TimeoutException("test 504"); });

app.MapGet("/cod/test", 
    [EnableCors("AnyOrigins")] 
    [ResponseCache(CacheProfileName = "NoCache")] 
    () => 
        Results.Text("<script nonce='23a98b38c'>"+"window.alert('Your client supports Javascript!" + "\\r\\n\\r\\n" +
        $"Server time (UTC): {DateTime.UtcNow.ToString("o")}" +
        "\\r\\n" +
        "Client time (UTC): ' + new Date().toISOString());" +
        "</script>" +
        "<noscript>Your client does not suppot Javascript</noscript>",
        "text/html"
        ));

app.MapGet("/cache/test/1", 
    [EnableCors("AnyOrigins")]
    (HttpContext context) =>
    {
        context.Response.Headers["cache-control"] = "no-cache, no-store";
        return Results.Ok();
    });

app.MapGet("/cache/test/2",
    [EnableCors("AnyOrigins")]
    (HttpContext context) =>
    {
        return Results.Ok();
    });

app.MapGet("/auth/test/1",
    [Authorize]
[SwaggerOperation(
Tags = new[] { "Auth" },
Summary = "Auth test #1 (authenticated users).",
Description = "Returns 200 - OK if called by " + 
"an authenticated user regardless of its role(s).")]
[SwaggerResponse(StatusCodes.Status200OK,"Authorized")]
[SwaggerResponse(StatusCodes.Status401Unauthorized, "Not authorized")]
[EnableCors("AnyOrigin")]
[ResponseCache(NoStore = true)] () =>
    {
        return Results.Ok("You are authorized!");
    });

app.MapGet("/auth/test/2",
    [Authorize(Roles = RoleNames.Moderator)]
[EnableCors("AnyOrigins")]
[SwaggerOperation(
Tags = new[] { "Auth" },
Summary = "Auth test #2 (Moderator role).",
Description = "Returns 200 - OK status code if called by " +
"an authenticated user assigned to the Moderator role.")]
[ResponseCache(NoStore = true)] () =>
    {
        return Results.Ok("You are authorized!");
    });

app.MapGet("/auth/test/3",
    [Authorize(Roles = RoleNames.Administrator)]
[EnableCors("AnyOrigins")]
[SwaggerOperation(
Tags = new[] { "Auth" },
Summary = "Auth test #3 (Administrator role).",
Description = "Returns 200 - OK if called by " +
"an authenticated user assigned to the Administrator role.")]
[ResponseCache(NoStore = true)] () =>
    {
        return Results.Ok("You are authorized!");
    });

app.MapGet("/auth/test/4",
    [Authorize(Roles = RoleNames.SuperAdmin)]
[ResponseCache(NoStore = true)]
[EnableCors("AnyOrigins")]
    () =>
    {
        return Results.Ok("You are authorized");
    });
#endregion

app.MapControllers();

app.Run();
