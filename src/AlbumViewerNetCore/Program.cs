using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using Newtonsoft.Json.Serialization;
using System.Text;
using System.Text.Encodings.Web;
using AlbumViewerAspNetCore;
using Microsoft.Extensions.Configuration;
using AlbumViewerBusiness;
using AlbumViewerBusiness.Configuration;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Net.Http.Headers;
using Microsoft.OpenApi.Models;


var builder = WebApplication.CreateBuilder(args);
var services = builder.Services;
var configuration = builder.Configuration;

var host = builder.Host;
var webHost = builder.WebHost;
var environment = builder.Environment;


services.AddDbContext<AlbumViewerContext>(builder =>
{
    string useSqLite = configuration["Data:useSqLite"];
    if (useSqLite != "true")
    {
        var connStr = configuration["Data:SqlServerConnectionString"];
        builder.UseSqlServer(connStr, opt => opt.EnableRetryOnFailure());
    }
    else
    {
        // Note this path has to have full  access for the Web user in order
        // to create the DB and write to it.
        var connStr = "Data Source=" +
                      Path.Combine(environment.ContentRootPath, "AlbumViewerData.sqlite");
        builder.UseSqlite(connStr);
    }
});


var config = new ApplicationConfiguration();
configuration.Bind("Application", config);
services.AddSingleton(config);

App.Configuration = config;

// Also make top level configuration available (for EF configuration and access to connection string)
services.AddSingleton<IConfigurationRoot>(configuration);
services.AddSingleton<IConfiguration>(configuration);

// Cors policy is added to controllers via [EnableCors("CorsPolicy")]
// or .UseCors("CorsPolicy") globally
services.AddCors(options =>
{
    options.AddPolicy("CorsPolicy",
        builder => builder
            // required if AllowCredentials is set also
            .SetIsOriginAllowed(s => true)
            //.AllowAnyOrigin()
            .AllowAnyMethod()  // doesn't work for DELETE!
            .WithMethods("DELETE")
            .AllowAnyHeader()
            .AllowCredentials()
    );
});

services.AddAuthentication(options => // JwtBearerDefaults.AuthenticationScheme)
    {
        options.DefaultScheme = "JWT_OR_COOKIE";
        options.DefaultChallengeScheme = "JWT_OR_COOKIE";
    })
    .AddCookie( options =>
    {
        options.LoginPath = "/login";
        options.ExpireTimeSpan = TimeSpan.FromDays(1);
    })
    .AddJwtBearer( options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = config.JwtToken.Issuer,
            ValidateAudience = true,
            ValidAudience = config.JwtToken.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(config.JwtToken.SigningKey))
        };
    })
    // Add this to allow both Cookies and Bearer Tokens 
    // - using default scheme names. Can use custom names and then add to the AddXXXX(scheme, options=> {} )
    .AddPolicyScheme("JWT_OR_COOKIE", "JWT_OR_COOKIE", options =>
    {
        options.ForwardDefaultSelector = context =>
        {
            string authorization = context.Request.Headers[HeaderNames.Authorization];
            if (!string.IsNullOrEmpty(authorization) && authorization.StartsWith("Bearer "))
            {
                return JwtBearerDefaults.AuthenticationScheme;
            }

            return CookieAuthenticationDefaults.AuthenticationScheme;
        };
    });

// Instance injection
services.AddScoped<AlbumRepository>();
services.AddScoped<ArtistRepository>();
services.AddScoped<AccountRepository>();

// Per request injections
services.AddScoped<ApiExceptionFilter>();

services.AddControllers()
    // Use classic JSON
    .AddNewtonsoftJson(opt =>
    {
        var resolver = opt.SerializerSettings.ContractResolver;
        if (resolver != null)
        {
            var res = resolver as DefaultContractResolver;
            res.NamingStrategy = null;
        }

        if (environment.IsDevelopment())
            opt.SerializerSettings.Formatting = Newtonsoft.Json.Formatting.Indented;
    });


builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Version = "v1",
        Title = "West Wind Album Viewer",
        Description = "An ASP.NET Core Sample API SPA application letting you browse and edit music albums and artists.",
        //TermsOfService = new Uri("https://example.com/terms"),
        //Contact = new OpenApiContact
        //{
        //    Name = "Example Contact",
        //    Url = new Uri("https://example.com/contact")
        //},
        //License = new OpenApiLicense
        //{
        //    Name = "Example License",
        //    Url = new Uri("https://example.com/license")
        //}
    });

    var filePath = Path.Combine(System.AppContext.BaseDirectory, "AlbumViewerNetCore.xml");
    options.IncludeXmlComments(filePath);
});

//
// *** BUILD THE APP
//
var app = builder.Build();


// Get any injected items
var albumContext = app.Services.CreateScope().ServiceProvider.GetService<AlbumViewerContext>();

    

//Log.Logger = new LoggerConfiguration()
//        .WriteTo.RollingFile(pathFormat: "logs\\log-{Date}.log")
//        .CreateLogger();

//loggerFactory
//    .AddSerilog();


if (environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();

    //app.UseDatabaseErrorPage();
}
else
{
    app.UseExceptionHandler(errorApp =>

            // Application level exception handler here - this is just a place holder
            errorApp.Run(async (context) =>
            {
                context.Response.StatusCode = 500;
                context.Response.ContentType = "text/html";
                await context.Response.WriteAsync("<html><body>\r\n");
                await context.Response.WriteAsync(
                        "We're sorry, we encountered an un-expected issue with your application.<br>\r\n");

                            // Capture the exception
                            var error = context.Features.Get<IExceptionHandlerFeature>();
                if (error != null)
                {
                                // This error would not normally be exposed to the client
                                await
                        context.Response.WriteAsync("<br>Error: " +
                                                    HtmlEncoder.Default.Encode(error.Error.Message) +
                                                    "<br>\r\n");
                }
                await context.Response.WriteAsync("<br><a href=\"/\">Home</a><br>\r\n");
                await context.Response.WriteAsync("</body></html>\r\n");
                await context.Response.WriteAsync(new string(' ', 512)); // Padding for IE
                        }));
}

//app.UseHttpsRedirection();


app.UseStatusCodePages();
app.UseDefaultFiles(); // so index.html is not required
app.UseStaticFiles();

app.UseRouting();

app.UseCors("CorsPolicy");

app.UseAuthentication();
app.UseAuthorization();

// check Swagger authentication
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    if (path.Value.Contains("/swagger/", StringComparison.OrdinalIgnoreCase))
    {
        if (!context.User.Identity.IsAuthenticated)
        {
            //context.Response.StatusCode = 401;
            //await context.Response.WriteAsync("Unauthorized");
            context.Response.Redirect("/login");
            return;
        }
    }

    await next();
});

// don't use the new simpler syntax as it doesn't terminate
// and always fires the catch-all route below
// if you don't have a catch-all route then this syntax is preferrable
// app.MapControllers();


// endpoint handler terminates and allows for catch-all middleware below
app.UseEndpoints(app =>
{
    app.MapControllers();
});


// for this app make it public
if (true)  // (app.Environment.IsDevelopment()) 
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// catch-all handler for HTML5 client routes - serve index.html
app.Run(async context =>
{
    var path = context.Request.Path.Value;

    // Make sure Angular output was created in wwwroot
    // Running Angular in dev mode nukes output folder!
    // so it could be missing.
    if (environment.WebRootPath == null)
        throw new InvalidOperationException("wwwroot folder doesn't exist. Please recompile your Angular Project before accessing index.html. API calls will work fine.");

    context.Response.ContentType = "text/html";
    await context.Response.SendFileAsync(Path.Combine(environment.WebRootPath, "index.html"));
});

// Initialize Database if it doesn't exist
AlbumViewerDataImporter.EnsureAlbumData(albumContext, Path.Combine(environment.ContentRootPath, "albums.js"));
albumContext?.Dispose();


Console.ForegroundColor = ConsoleColor.DarkYellow;
Console.WriteLine($@"----------------
AlbumViewer Core
----------------");
Console.ResetColor();

Console.WriteLine("\r\nPlatform: " + System.Runtime.InteropServices.RuntimeInformation.OSDescription);
Console.WriteLine(".NET Version: " + System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription);
Console.WriteLine("Hosting Environment: " + environment.EnvironmentName);
string useSqLite = configuration["Data:useSqLite"];
Console.WriteLine(useSqLite == "true" ? "SqLite" : "Sql Server");



app.Run();
