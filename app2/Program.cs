using app2.Models;
using Microsoft.AspNetCore.Cors.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddControllers(); // Add controllers for API endpoints

// Configure CORS for cross-domain communication
// app2's client (HTML/JS) will be served from its own origin, but app1 (parent iframe)
// will be on a different origin. Also, app1's backend will call app2's backend.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowApp1AndSelf", policy =>
    {
        // IMPORTANT: Replace "http://localhost:7001" and "https://app1.yourdomain.com"
        // with the actual origin(s) where your app1 will be hosted.
        // Also include app2's own origin if its client-side JS makes direct calls to its backend.
        policy.WithOrigins("http://localhost:5137", "https://app1.yourdomain.com", "http://localhost:5119", "https://app2.yourdomain.com")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Needed if you plan to send cookies/auth headers (though not strictly necessary for this OTT flow).
    });
});

builder.Services.AddHttpClient(); // Ensure HttpClient is added to the service collection.

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/ms-netcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

// Apply the CORS policy BEFORE Authorization and Endpoint routing
app.UseCors("AllowApp1AndSelf");

app.UseAuthorization();

app.MapRazorPages();
app.MapControllers(); // Map API controllers

app.Run();