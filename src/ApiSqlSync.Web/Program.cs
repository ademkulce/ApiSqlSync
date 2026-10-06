using ApiSqlSync.Core.Abstractions;
using ApiSqlSync.SqlServer.Configuration;
using ApiSqlSync.SqlServer.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Ortak ayarların üzerine kişisel ayarlar gelir.
// Sunucuda gerekirse uygulamaya ait ortam değişkenleriyle değiştirilebilir.
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true, reloadOnChange: false).AddEnvironmentVariables(prefix: "APISQLSYNC_");

var connectionString =
    builder.Configuration["SqlServer:ConnectionString"];

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException("SqlServer:ConnectionString is missing. " + "Check appsettings.Local.json.");
}

var sqlOptions = new SqlServerOptions
{
    ConnectionString = connectionString.Trim(),
    CommandTimeoutSeconds = builder.Configuration.GetValue<int>("SqlServer:CommandTimeoutSeconds", 30)
};

// Ayar hatalarını ilk sayfa isteğinden önce yakalayalım.
sqlOptions.Validate();

builder.Services.AddSingleton(sqlOptions);

// Her web isteği kendi okuma servisini kullanır.
builder.Services.AddScoped<ISyncJobReader, SqlServerSyncJobReader>();
builder.Services.AddScoped<ISyncRunReader, SqlServerSyncRunReader>();

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();