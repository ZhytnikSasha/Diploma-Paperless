using Microsoft.AspNetCore.Identity;
using Paperless.Models;

namespace Paperless.Data
{
    public static class DbSeeder
    {
        public static async Task SeedRolesAndAdminAsync(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            string[] roleNames = { "Адміністратор", "Менеджер", "Співробітник" };

            foreach (var roleName in roleNames)
            {
                var roleExist = await roleManager.RoleExistsAsync(roleName);
                if (!roleExist)
                {
                    await roleManager.CreateAsync(new IdentityRole(roleName));
                }
            }

            string adminEmail = "admin@paperless.local";
            var adminUser = await userManager.FindByEmailAsync(adminEmail);

            if (adminUser == null)
            {
                var newAdmin = new ApplicationUser
                {
                    UserName = adminEmail,
                    Email = adminEmail,
                    FirstName = "Головний",
                    LastName = "Адміністратор",
                    Position = "Системний адміністратор",
                    Department = "IT-відділ",
                    EmailConfirmed = true
                };

                string adminPassword = "AdminPassword123!";
                var createPowerUser = await userManager.CreateAsync(newAdmin, adminPassword);

                if (createPowerUser.Succeeded)
                {
                    await userManager.AddToRoleAsync(newAdmin, "Адміністратор");
                }
            }
        }
    }
}