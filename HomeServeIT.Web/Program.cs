using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using HomeServeIT.Web.Data;
using HomeServeIT.Web.Models;
using QuestPDF.Infrastructure;

QuestPDF.Settings.License = LicenseType.Community;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");
builder.Services.AddSingleton(ServerVersion.AutoDetect(connectionString));
builder.Services.AddDbContext<ApplicationDbContext>((sp, options) =>
    options.UseMySql(connectionString, sp.GetRequiredService<ServerVersion>()));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();
builder.Services.AddDataProtection()
    .SetApplicationName("HomeServeIT.Web");

builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.User.RequireUniqueEmail = true;
        options.Lockout.AllowedForNewUsers = true;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Password.RequiredLength = 8;
        options.Password.RequiredUniqueChars = 1;
        options.Password.RequireUppercase = true;
        options.Password.RequireLowercase = true;
        options.Password.RequireDigit = true;
        options.Password.RequireNonAlphanumeric = true;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<ApplicationDbContext>();
builder.Services.ConfigureApplicationCookie(options =>
{
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromDays(30);
    options.SlidingExpiration = true;
    options.LoginPath = "/Identity/Account/Login";
    options.AccessDeniedPath = "/Identity/Account/AccessDenied";
});
builder.Services.AddControllersWithViews();
builder.Services.AddSignalR();
builder.Services.AddScoped<HomeServeIT.Web.Services.NotificationService>();
builder.Services.AddScoped<HomeServeIT.Web.Services.ApplicationSettingsService>();
builder.Services.AddHostedService<HomeServeIT.Web.Services.NotificationWorker>();
builder.Services.AddScoped<HomeServeIT.Web.Services.JobInventoryService>();
builder.Services.AddScoped<HomeServeIT.Web.Services.InventoryCatalogService>();
builder.Services.AddScoped<HomeServeIT.Web.Services.TechnicianAssignmentService>();
builder.Services.AddScoped<HomeServeIT.Web.Services.AccountProfileService>();
builder.Services.AddScoped<HomeServeIT.Web.Services.PrivateUploadService>();
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = HomeServeIT.Web.Services.PrivateUploadService.MaxRequestBytes);
builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(options =>
    options.MultipartBodyLengthLimit = HomeServeIT.Web.Services.PrivateUploadService.MaxRequestBytes);
var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

// The Development HTTP launch profile intentionally has no HTTPS listener.
if (!app.Environment.IsDevelopment() || builder.Configuration.GetValue<int?>("HTTPS_PORT") != null
    || (builder.Configuration["urls"] ?? "").Contains("https://", StringComparison.OrdinalIgnoreCase))
    app.UseHttpsRedirection();
// Rewrite legacy URLs before static asset routing; existing private files must never bypass authorization.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/uploads", StringComparison.OrdinalIgnoreCase, out var remainder))
        context.Request.Path = "/private-files" + remainder;
    await next(context);
});
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.MapRazorPages()
   .WithStaticAssets();

app.MapHub<HomeServeIT.Web.Hubs.ChatHub>("/chathub");

using (var scope = app.Services.CreateScope())
{
    if (args.Contains("--reset-test-data") || args.Contains("--delete-test-data"))
    {
        var deleteOnly = args.Contains("--delete-test-data");
        await DbInitializer.ResetTestDataAsync(scope.ServiceProvider, app.Environment, deleteOnly);
        Console.WriteLine(deleteOnly ? "Disposable test database deleted." : "Disposable test database reset and seeded.");
        return;
    }
    if (args.Contains("--check-completion-dates"))
    {
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTime.UtcNow;
        var invalidIds = await context.ServiceRequests.AsNoTracking()
            .Where(r => r.Status == "Completed" && (!r.CompletedDate.HasValue
                || r.CompletedDate < r.ScheduledDate || r.CompletedDate > now))
            .Select(r => r.RequestID).ToListAsync();
        Console.WriteLine($"Completed requests requiring source-record review: {invalidIds.Count}");
        foreach (var id in invalidIds) Console.WriteLine($"RequestID: {id}");
        return; // Read-only: never guess historical dates.
    }
    if (args.Contains("--initialize-database"))
    {
        await DbInitializer.InitializeAsync(scope.ServiceProvider, app.Environment);
        return; // Explicit deployment/maintenance action, never normal production startup.
    }
    if (app.Environment.IsDevelopment())
        await DbInitializer.InitializeAsync(scope.ServiceProvider, app.Environment);
}

app.Run();
