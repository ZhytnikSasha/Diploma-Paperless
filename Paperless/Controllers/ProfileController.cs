using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paperless.Data;
using Paperless.Models;
using Paperless.Models.ViewModels;

namespace Paperless.Controllers
{
    [Authorize]
    public class ProfileController : Controller
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AppDbContext _context;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public ProfileController(UserManager<ApplicationUser> userManager, AppDbContext context, IWebHostEnvironment webHostEnvironment)
        {
            _userManager = userManager;
            _context = context;
            _webHostEnvironment = webHostEnvironment;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            var roles = await _userManager.GetRolesAsync(user);
            var userRole = roles.FirstOrDefault() ?? "Співробітник";

            var myDocs = await _context.Documents
                .Where(d => d.AuthorId == user.Id)
                .OrderByDescending(d => d.CreatedAt)
                .ToListAsync();

            var model = new UserProfileViewModel
            {
                Id = user.Id,
                Email = user.Email ?? string.Empty,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Position = user.Position,
                Department = user.Department,
                Role = userRole,
                AvatarPath = user.AvatarPath,
                MyDocuments = myDocs
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(UserProfileViewModel model)
        {
            var user = await _userManager.GetUserAsync(User);
            if (user == null) return NotFound();

            if (!ModelState.IsValid)
            {
                model.MyDocuments = await _context.Documents
                    .Where(d => d.AuthorId == user.Id)
                    .OrderByDescending(d => d.CreatedAt)
                    .ToListAsync();

                return View("Index", model);
            }

            user.FirstName = model.FirstName;
            user.LastName = model.LastName;
            user.Position = model.Position;
            user.Department = model.Department;

            if (!string.IsNullOrEmpty(model.NewPassword) && !string.IsNullOrEmpty(model.CurrentPassword))
            {
                var changeResult = await _userManager.ChangePasswordAsync(user, model.CurrentPassword, model.NewPassword);
                if (!changeResult.Succeeded)
                {
                    foreach (var error in changeResult.Errors) ModelState.AddModelError(string.Empty, error.Description);
                    return View("Index", model);
                }
            }

            if (model.AvatarFile != null)
            {
                string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "avatars");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                string fileName = user.Id + Path.GetExtension(model.AvatarFile.FileName);
                string filePath = Path.Combine(uploadsFolder, fileName);

                using (var fileStream = new FileStream(filePath, FileMode.Create))
                {
                    await model.AvatarFile.CopyToAsync(fileStream);
                }
                user.AvatarPath = "/avatars/" + fileName;
            }

            await _userManager.UpdateAsync(user);
            TempData["SuccessMessage"] = "Профіль оновлено!";
            return RedirectToAction(nameof(Index));
        }
    }
}