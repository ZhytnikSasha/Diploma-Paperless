using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Paperless.Data;
using Paperless.Models;
using Paperless.Models.Enums;

namespace Paperless.Controllers
{
    [Authorize]
    public class DocumentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly IWebHostEnvironment _webHostEnvironment;

        public DocumentController(AppDbContext context, UserManager<ApplicationUser> userManager, IWebHostEnvironment webHostEnvironment)
        {
            _context = context;
            _userManager = userManager;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<IActionResult> Index(string? searchString, DocumentStatus? statusFilter)
        {
            ViewData["CurrentSearch"] = searchString;
            ViewData["CurrentFilter"] = (int?)statusFilter;
            var query = _context.Documents
                .Include(d => d.Author)
                .AsQueryable();

            if (!string.IsNullOrEmpty(searchString))
            {
                query = query.Where(d =>
                    d.Title.Contains(searchString) ||
                    (d.Description != null && d.Description.Contains(searchString)));
            }
            if (statusFilter.HasValue)
            {
                query = query.Where(d => d.Status == statusFilter.Value);
            }

            var documents = await query.OrderByDescending(d => d.CreatedAt).ToListAsync();

            return View(documents);
        }

        public IActionResult Create()
        {
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Document model, IFormFile? uploadedFile)
        {
            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            model.AuthorId = currentUser.Id;

            ModelState.Remove("Author");
            ModelState.Remove("AuthorId");

            if (ModelState.IsValid)
            {
                if (uploadedFile != null && uploadedFile.Length > 0)
                {
                    var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".rtf" };
                    var fileExtension = Path.GetExtension(uploadedFile.FileName).ToLowerInvariant();
                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        ModelState.AddModelError(string.Empty, $"Помилка: Файли формату {fileExtension} не підтримуються. Завантажуйте лише документи (PDF, Word, Excel, TXT).");
                        return View(model);
                    }
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }
                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + uploadedFile.FileName;
                    string filePath = Path.Combine(uploadsFolder, uniqueFileName);
                    using (var fileStream = new FileStream(filePath, FileMode.Create))
                    {
                        await uploadedFile.CopyToAsync(fileStream);
                    }

                    model.FilePath = "/uploads/" + uniqueFileName;
                    model.OriginalFileName = uploadedFile.FileName;
                }
                _context.Documents.Add(model);
                await _context.SaveChangesAsync();

                var auditRecord = new DocumentHistory
                {
                    DocumentId = model.Id,
                    DocumentTitle = model.Title,
                    UserId = currentUser.Id,
                    OldStatus = null,
                    NewStatus = DocumentStatus.Draft,
                    ChangedAt = DateTime.Now
                };
                _context.DocumentHistories.Add(auditRecord);
                await _context.SaveChangesAsync();

                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var document = await _context.Documents.FindAsync(id);
            if (document == null) return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            if (document.AuthorId != currentUserId && !User.IsInRole("Адміністратор")) return Forbid();

            return View(document);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Document model, IFormFile? newUploadedFile)
        {
            if (id != model.Id) return NotFound();

            var document = await _context.Documents.FindAsync(id);
            if (document == null) return NotFound();

            var currentUserId = _userManager.GetUserId(User);
            if (document.AuthorId != currentUserId && !User.IsInRole("Адміністратор")) return Forbid();

            ModelState.Remove("Author");
            ModelState.Remove("AuthorId");

            if (ModelState.IsValid)
            {
                document.Title = model.Title;
                document.Description = model.Description;
                document.UpdatedAt = DateTime.Now;

                if (newUploadedFile != null && newUploadedFile.Length > 0)
                {
                    var allowedExtensions = new[] { ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".txt", ".rtf" };
                    var fileExtension = Path.GetExtension(newUploadedFile.FileName).ToLowerInvariant();

                    if (!allowedExtensions.Contains(fileExtension))
                    {
                        ModelState.AddModelError(string.Empty, $"Помилка: Файли формату {fileExtension} не підтримуються.");
                        return View(document);
                    }
                    if (!string.IsNullOrEmpty(document.FilePath))
                    {
                        var oldFileName = document.FilePath.Split('/').Last();
                        var oldFilePath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", oldFileName);

                        if (System.IO.File.Exists(oldFilePath))
                        {
                            try
                            {
                                System.IO.File.Delete(oldFilePath);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Не вдалося видалити старий файл: {ex.Message}");
                            }
                        }
                    }
                    string uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads");
                    if (!Directory.Exists(uploadsFolder))
                    {
                        Directory.CreateDirectory(uploadsFolder);
                    }

                    string uniqueFileName = Guid.NewGuid().ToString() + "_" + newUploadedFile.FileName;
                    string newFilePath = Path.Combine(uploadsFolder, uniqueFileName);

                    using (var fileStream = new FileStream(newFilePath, FileMode.Create))
                    {
                        await newUploadedFile.CopyToAsync(fileStream);
                    }
                    document.FilePath = "/uploads/" + uniqueFileName;
                    document.OriginalFileName = newUploadedFile.FileName;
                }

                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }

            return View(model);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var document = await _context.Documents.FindAsync(id);
            if (document != null)
            {
                var currentUserId = _userManager.GetUserId(User);
                if (document.AuthorId != currentUserId && !User.IsInRole("Адміністратор")) return Forbid();
                if (!string.IsNullOrEmpty(document.FilePath))
                {
                    var fileName = document.FilePath.Split('/').Last();
                    var filePath = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", fileName);

                    if (System.IO.File.Exists(filePath))
                    {
                        try { System.IO.File.Delete(filePath); } catch { }
                    }
                }
                var auditRecord = new DocumentHistory
                {
                    DocumentId = null,
                    DocumentTitle = document.Title,
                    UserId = currentUserId,
                    OldStatus = document.Status,
                    NewStatus = DocumentStatus.Deleted,
                    ChangedAt = DateTime.Now
                };
                _context.DocumentHistories.Add(auditRecord);
                _context.Documents.Remove(document);

                await _context.SaveChangesAsync();
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> ChangeStatus(int id, DocumentStatus newStatus)
        {
            var document = await _context.Documents.FindAsync(id);
            if (document == null) return NotFound();

            var currentUser = await _userManager.GetUserAsync(User);
            if (currentUser == null) return Challenge();

            bool isManagerOrAdmin = User.IsInRole("Менеджер") || User.IsInRole("Адміністратор");
            bool statusChanged = false;
            var oldStatus = document.Status;

            if (document.AuthorId == currentUser.Id && document.Status == DocumentStatus.Draft && newStatus == DocumentStatus.PendingReview)
            {
                document.Status = newStatus;
                statusChanged = true;
            }
            else if (isManagerOrAdmin && document.Status == DocumentStatus.PendingReview &&
                    (newStatus == DocumentStatus.Approved || newStatus == DocumentStatus.Rejected))
            {
                document.Status = newStatus;
                statusChanged = true;
            }
            else
            {
                TempData["ErrorMessage"] = "У вас немає прав для цієї дії або зміна статусу неможлива.";
                return RedirectToAction(nameof(Index));
            }
            if (statusChanged)
            {
                document.UpdatedAt = DateTime.Now;

                var auditRecord = new DocumentHistory
                {
                    DocumentId = document.Id,
                    DocumentTitle = document.Title,
                    UserId = currentUser.Id,
                    OldStatus = oldStatus,
                    NewStatus = newStatus,
                    ChangedAt = DateTime.Now
                };

                _context.DocumentHistories.Add(auditRecord);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = "Статус документа успішно оновлено!";
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Download(int? id)
        {
            if (id == null) return NotFound();

            var document = await _context.Documents.FindAsync(id);
            if (document == null || string.IsNullOrEmpty(document.FilePath))
            {
                return NotFound("Файл не знайдено.");
            }
            var absolutePath = Path.Combine(_webHostEnvironment.WebRootPath, document.FilePath.TrimStart('/'));

            if (!System.IO.File.Exists(absolutePath))
            {
                return NotFound("Фізичний файл був видалений або переміщений.");
            }
            var downloadFileName = !string.IsNullOrEmpty(document.OriginalFileName)
                ? document.OriginalFileName
                : Path.GetFileName(document.FilePath);

            var contentType = "application/octet-stream";
            return PhysicalFile(absolutePath, contentType, downloadFileName);
        }
    }
}