using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paperless.Data;
using Paperless.Models;
using Paperless.Models.ViewModels;

namespace Paperless.Controllers
{
    [Authorize(Roles = "Адміністратор")]
    public class AdminController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly AppDbContext _context;
        public AdminController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager, AppDbContext context)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _context = context;
        }
        [HttpGet]
        public async Task<IActionResult> AuditLog()
        {
            var logs = await _context.DocumentHistories
                .Include(h => h.Document)
                .Include(h => h.User)
                .OrderByDescending(h => h.ChangedAt)
                .ToListAsync();

            return View(logs);
        }
        public async Task<IActionResult> Index()
        {
            var users = await _userManager.Users.ToListAsync();
            var userViewModels = new List<UserViewModel>();

            foreach (var user in users)
            {
                var roles = await _userManager.GetRolesAsync(user);
                userViewModels.Add(new UserViewModel
                {
                    Id = user.Id,
                    FullName = user.FullName,
                    Email = user.Email ?? string.Empty,
                    Position = string.IsNullOrEmpty(user.Position) ? "Не вказано" : user.Position,
                    Roles = string.Join(", ", roles)
                });
            }

            return View(userViewModels);
        }

        [HttpGet]
        public async Task<IActionResult> ManageRoles(string userId)
        {
            ViewBag.userId = userId;
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();

            ViewBag.UserName = user.FullName;

            var model = new List<ManageUserRolesViewModel>();

            foreach (var role in await _roleManager.Roles.ToListAsync())
            {
                var userRolesViewModel = new ManageUserRolesViewModel
                {
                    RoleName = role.Name ?? string.Empty,
                    IsSelected = await _userManager.IsInRoleAsync(user, role.Name ?? string.Empty)
                };
                model.Add(userRolesViewModel);
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ManageRoles(List<ManageUserRolesViewModel> model, string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return NotFound();
            var roles = await _userManager.GetRolesAsync(user);
            var result = await _userManager.RemoveFromRolesAsync(user, roles);

            if (!result.Succeeded)
            {
                ModelState.AddModelError("", "Не вдалося видалити існуючі ролі");
                return View(model);
            }
            var rolesToAdd = model.Where(x => x.IsSelected).Select(y => y.RoleName);
            result = await _userManager.AddToRolesAsync(user, rolesToAdd);

            if (!result.Succeeded)
            {
                ModelState.AddModelError("", "Не вдалося додати обрані ролі");
                return View(model);
            }

            TempData["SuccessMessage"] = $"Ролі для {user.FullName} успішно оновлено!";
            return RedirectToAction(nameof(Index));
        }
    }
}