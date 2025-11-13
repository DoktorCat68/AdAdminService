using AdAdminPortal.Services;
using Microsoft.AspNetCore.Authentication.Negotiate;

var builder = WebApplication.CreateBuilder(args);

// Читаем список групп-админов из конфигурации
var adminGroupsSection = builder.Configuration.GetSection("Ad:AdminGroups");
string[] adminGroups = adminGroupsSection.Exists()
    ? adminGroupsSection.Get<string[]>() ?? Array.Empty<string>()
    : Array.Empty<string>();

builder.Services.AddAuthentication(NegotiateDefaults.AuthenticationScheme)
    .AddNegotiate();

builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("AdAdminsOnly", policy =>
    {
        // Требуем аутентификацию
        policy.RequireAuthenticatedUser();

        // Дополнительно проверяем группы
        policy.RequireAssertion(context =>
        {
        var user = context.User;

        // Нет пользователя или он не аутентифицирован
        if (user == null || user.Identity == null || !user.Identity.IsAuthenticated)
            {
            return false;
        }

        // Если группы админов не настроены в appsettings.json,
        // ведём себя как раньше: пускаем любого аутентифицированного
        if (adminGroups.Length == 0)
        {
            return true;
        }

        // Если пользователь состоит хоть в одной из указанных групп — пускаем
        foreach (var group in adminGroups)
        {
            if (!string.IsNullOrWhiteSpace(group) && user.IsInRole(group))
            {
                return true;
            }
        }

        // Ни в одной админской группе не состоит
        return false;
    });
});
});

builder.Services.AddControllersWithViews();
builder.Services.AddScoped<IAdService, AdService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=AdUsers}/{action=Index}/{id?}");

app.Run();