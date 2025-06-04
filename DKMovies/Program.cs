using Microsoft.EntityFrameworkCore;
using DKMovies.Models;
using DKMovies.Middleware; // Thêm using cho middleware

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllersWithViews();

// Add session service to handle login/logout state
builder.Services.AddDistributedMemoryCache(); // Enable memory cache for session storage
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Set session timeout
    options.Cookie.HttpOnly = true; // Ensure the session cookie is only accessible via HTTP
    options.Cookie.IsEssential = true; // Make the session cookie essential for the app to function
});

// Add DbContext for SQL Server connection
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHttpContextAccessor();

// Cấu hình Authentication
builder.Services.AddAuthentication("MyCookieAuth")
    .AddCookie("MyCookieAuth", options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.Cookie.Name = "DKMovies.Auth";
        options.ExpireTimeSpan = TimeSpan.FromDays(30);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

// Thêm Authorization policies
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdminOnly", policy =>
        policy.RequireClaim("UserType", "Admin"));

    options.AddPolicy("SuperAdminOnly", policy =>
        policy.RequireClaim("UserType", "Admin")
               .RequireClaim(System.Security.Claims.ClaimTypes.Role, "SuperAdmin"));

    options.AddPolicy("UserOnly", policy =>
        policy.RequireClaim("UserType", "User"));
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

// Add session middleware before routing
app.UseSession();
app.UseRouting();
app.UseAuthentication();

// Thêm middleware bảo vệ admin routes
app.UseMiddleware<AdminAccessMiddleware>();

// Authorization middleware (to check session state for role-based access)
app.UseAuthorization();

// Thêm route cho admin
app.MapControllerRoute(
    name: "admin",
    pattern: "Admin/{action=Dashboard}/{id?}",
    defaults: new { controller = "Admin" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");



// Run the application
app.Run();

// Method để tạo dữ liệu mặc định


// Helper method để hash password
string HashPassword(string password)
{
    using var sha256 = System.Security.Cryptography.SHA256.Create();
    byte[] bytes = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password));
    return Convert.ToBase64String(bytes);
}