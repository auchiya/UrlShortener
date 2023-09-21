using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Text.RegularExpressions;
using UrlShortener;
using UrlShortener.Entities;
using UrlShortener.Extensions;
using UrlShortener.Models;
using UrlShortener.Services;

var MyAllowSpecificOrigins = "_myAllowSpecificOrigins";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddPolicy(name: MyAllowSpecificOrigins,
                      policy =>
                      {
                          policy.AllowAnyOrigin();
                          policy.AllowAnyMethod();
                          policy.AllowAnyHeader();
                      });
});

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<ApplicationDbContext>(o =>
    o.UseSqlServer(builder.Configuration.GetConnectionString("Database")));

builder.Services.AddScoped<UrlShorteningService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    app.ApplyMigrations();
}


app.MapPost("api/shorten", async (
    ShortenUrlRequest request,
    UrlShorteningService urlShorteningService,
    ApplicationDbContext dbContext,
    HttpContext httpContext) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out _))
    {
        return Results.BadRequest("The specified URL is invalid.");
    }

    if (!request.Alias.IsNullOrEmpty())
    {
        var isAliasValid = Regex.IsMatch(request.Alias, "^[A-Za-z0-9]+$");
        if (!isAliasValid)
        {
            return Results.BadRequest("The specified Alias is not valid.");
        }

        var isTakenAlias = await dbContext.ShortenedUrls.AnyAsync(s => s.Alias == request.Alias);
        if (isTakenAlias)
        {
            return Results.BadRequest("The specified Alias is already taken.");
        }
    }

    var code = await urlShorteningService.GenerateUniqueCode();
    var shortenedUrl = new ShortenedUrl
    {
        Id = Guid.NewGuid(),
        LongUrl = request.Url,
        Code = code,
        Alias = request.Alias,
        ShortUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}/api/{code}",
        CreatedOnUtc = DateTime.Now
    };

    dbContext.ShortenedUrls.Add(shortenedUrl);

    await dbContext.SaveChangesAsync();

    return Results.Ok(shortenedUrl);
})
.WithOpenApi();

app.MapGet("api/{code}", async (string code, ApplicationDbContext dbContext) =>
{
    var shortenedUrl = await dbContext.ShortenedUrls.FirstOrDefaultAsync(s => s.Code == code);

    if (shortenedUrl == null)
    {
        return Results.NotFound(code);
    }

    return Results.Ok(shortenedUrl.LongUrl);
})
.WithOpenApi();

app.MapGet("api/list", async (ApplicationDbContext dbContext) =>
{
    var shortenedUrls = await dbContext.ShortenedUrls.ToListAsync();

    return Results.Ok(shortenedUrls);
})
.WithOpenApi();

app.UseCors(MyAllowSpecificOrigins);
app.UseHttpsRedirection();

app.Run();
