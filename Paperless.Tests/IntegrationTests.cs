using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Paperless.Controllers; // Замініть на ваш простір імен
using Paperless.Data;        // Замініть на ваш простір імен
using Paperless.Models;      // Замініть на ваш простір імен
using Paperless.Models.Enums;
using Paperless.Models.ViewModels;
using Microsoft.AspNetCore.Hosting;

namespace Paperless.Tests
{
    public class IntegrationTests : IDisposable
    {
        private readonly string _tempWebRoot;

        public IntegrationTests()
        {
            // Створюємо ізольовану тимчасову папку для кожного запуску тестів (імітація wwwroot)
            _tempWebRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempWebRoot);
        }

        public void Dispose()
        {
            // Прибираємо за собою файли після проходження тестів
            if (Directory.Exists(_tempWebRoot))
            {
                Directory.Delete(_tempWebRoot, true);
            }
        }

        #region Налаштування (Mocks & In-Memory DB)

        private AppDbContext GetInMemoryDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        private Mock<UserManager<ApplicationUser>> GetMockUserManager()
        {
            var store = new Mock<IUserStore<ApplicationUser>>();
            var mgr = new Mock<UserManager<ApplicationUser>>(store.Object, null, null, null, null, null, null, null, null);

            var fakeUser = new ApplicationUser { Id = "user-1", UserName = "test@mail.com" };
            mgr.Setup(x => x.GetUserId(It.IsAny<ClaimsPrincipal>())).Returns(fakeUser.Id);
            mgr.Setup(x => x.GetUserAsync(It.IsAny<ClaimsPrincipal>())).ReturnsAsync(fakeUser);

            return mgr;
        }

        private Mock<SignInManager<ApplicationUser>> GetMockSignInManager(UserManager<ApplicationUser> userManager)
        {
            var contextAccessor = new Mock<IHttpContextAccessor>();
            var claimsFactory = new Mock<IUserClaimsPrincipalFactory<ApplicationUser>>();
            return new Mock<SignInManager<ApplicationUser>>(userManager, contextAccessor.Object, claimsFactory.Object, null, null, null, null);
        }

        private void SetupControllerContext(Controller controller, string role = null, string userId = "user-1")
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Name, "test@mail.com")
            };

            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "mock"));

            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        }

        // Хелпер для створення моку файлу, який РЕАЛЬНО записується на диск під час тесту
        private Mock<IFormFile> GetMockFile(string fileName)
        {
            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.FileName).Returns(fileName);
            fileMock.Setup(f => f.Length).Returns(100);
            fileMock.Setup(f => f.CopyToAsync(It.IsAny<Stream>(), It.IsAny<CancellationToken>()))
                .Callback<Stream, CancellationToken>((stream, token) =>
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes("fake file content");
                    stream.Write(bytes, 0, bytes.Length);
                })
                .Returns(Task.CompletedTask);
            return fileMock;
        }

        #endregion

        #region TC_I101 - TC_I104: Автентифікація та Ролі

        [Fact]
        public async Task TC_I101_RegisterUser_SavesToDbAndAssignsRole()
        {
            var mockUserManager = GetMockUserManager();
            var mockSignInManager = GetMockSignInManager(mockUserManager.Object);

            mockUserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
            mockUserManager.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Співробітник")).ReturnsAsync(IdentityResult.Success);

            var controller = new AccountController(mockUserManager.Object, mockSignInManager.Object);
            SetupControllerContext(controller);

            var model = new RegisterViewModel { Email = "new@mail.com", Password = "Password1!", ConfirmPassword = "Password1!" };
            await controller.Register(model);

            // Перевіряємо інтеграцію: менеджер викликав створення і додав роль
            mockUserManager.Verify(x => x.CreateAsync(It.Is<ApplicationUser>(u => u.UserName == "new@mail.com"), "Password1!"), Times.Once);
            mockUserManager.Verify(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Співробітник"), Times.Once);
        }

        [Fact]
        public void TC_I102_CreateAccount_HashesPassword()
        {
            // Перевірка, що Identity коректно хешує пароль перед збереженням
            var hasher = new PasswordHasher<ApplicationUser>();
            var user = new ApplicationUser { UserName = "test@mail.com" };

            var hash = hasher.HashPassword(user, "MySecret123!");
            user.PasswordHash = hash;

            Assert.NotNull(user.PasswordHash);
            Assert.Contains(PasswordVerificationResult.Success.ToString(), hasher.VerifyHashedPassword(user, user.PasswordHash, "MySecret123!").ToString());
        }

        [Fact]
        public async Task TC_I103_Login_IssuesCookieSession()
        {
            var mockUserManager = GetMockUserManager();
            var mockSignInManager = GetMockSignInManager(mockUserManager.Object);
            mockSignInManager.Setup(x => x.PasswordSignInAsync("test@mail.com", "Pass1!", false, false)).ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

            var controller = new AccountController(mockUserManager.Object, mockSignInManager.Object);
            SetupControllerContext(controller);

            var model = new LoginViewModel { Email = "test@mail.com", Password = "Pass1!" };
            var result = await controller.Login(model) as RedirectToActionResult;

            Assert.NotNull(result);
            mockSignInManager.Verify(x => x.PasswordSignInAsync("test@mail.com", "Pass1!", false, false), Times.Once);
        }

        [Fact]
        public async Task TC_I104_Logout_EndsSession()
        {
            var mockUserManager = GetMockUserManager();
            var mockSignInManager = GetMockSignInManager(mockUserManager.Object);
            mockSignInManager.Setup(x => x.SignOutAsync()).Returns(Task.CompletedTask);

            var controller = new AccountController(mockUserManager.Object, mockSignInManager.Object);
            SetupControllerContext(controller);

            var result = await controller.Logout() as RedirectToActionResult;

            Assert.NotNull(result);
            mockSignInManager.Verify(x => x.SignOutAsync(), Times.Once);
        }

        #endregion

        #region TC_I201 - TC_I204: Робота з документами та Файловою системою

        [Fact]
        public async Task TC_I201_CreateDocument_SavesDbRecordAndPhysicalFile()
        {
            using var db = GetInMemoryDbContext();
            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns(_tempWebRoot);

            var controller = new DocumentController(db, GetMockUserManager().Object, mockEnv.Object);
            SetupControllerContext(controller);

            var fileMock = GetMockFile("testdoc.pdf");
            var document = new Document { Title = "Integration Doc" };

            await controller.Create(document, fileMock.Object);

            var savedDoc = db.Documents.FirstOrDefault();
            Assert.NotNull(savedDoc);
            Assert.Equal("Integration Doc", savedDoc.Title);
            Assert.NotNull(savedDoc.FilePath);

            // ПЕРЕВІРКА ІНТЕГРАЦІЇ З ФАЙЛОВОЮ СИСТЕМОЮ: Файл дійсно існує на диску!
            var physicalPath = Path.Combine(_tempWebRoot, savedDoc.FilePath.TrimStart('/'));
            Assert.True(File.Exists(physicalPath), "Фізичний файл не був збережений на диск!");
        }

        [Fact]
        public async Task TC_I202_EditDocument_ReplacesFileAndUpdatesDate()
        {
            using var db = GetInMemoryDbContext();
            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns(_tempWebRoot);

            var uploadsFolder = Path.Combine(_tempWebRoot, "uploads");
            Directory.CreateDirectory(uploadsFolder);
            var oldFileName = "old_file.pdf";
            var oldPhysicalPath = Path.Combine(uploadsFolder, oldFileName);
            File.WriteAllText(oldPhysicalPath, "old data");

            var doc = new Document { Id = 1, Title = "Old Title", FilePath = $"/uploads/{oldFileName}", AuthorId = "user-1", UpdatedAt = DateTime.Now.AddDays(-1) };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, mockEnv.Object);
            SetupControllerContext(controller);

            var newFileMock = GetMockFile("new_file.pdf");
            doc.Title = "New Title";

            await controller.Edit(1, doc, newFileMock.Object);

            var updatedDoc = db.Documents.Find(1);
            Assert.Equal("New Title", updatedDoc.Title);
            Assert.NotEqual($"/uploads/{oldFileName}", updatedDoc.FilePath);
            Assert.True(updatedDoc.UpdatedAt > DateTime.Now.AddMinutes(-1)); // Дата оновлена

            // Старий файл має бути видалений, а новий збережений
            Assert.False(File.Exists(oldPhysicalPath), "Старий файл не був видалений!");
            var newPhysicalPath = Path.Combine(_tempWebRoot, updatedDoc.FilePath.TrimStart('/'));
            Assert.True(File.Exists(newPhysicalPath), "Новий файл не був збережений!");
        }

        [Fact]
        public async Task TC_I203_DeleteDocument_RemovesDbRecordAndPhysicalFile()
        {
            using var db = GetInMemoryDbContext();
            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns(_tempWebRoot);

            var uploadsFolder = Path.Combine(_tempWebRoot, "uploads");
            Directory.CreateDirectory(uploadsFolder);
            var fileName = "delete_me.pdf";
            var physicalPath = Path.Combine(uploadsFolder, fileName);
            File.WriteAllText(physicalPath, "data");

            var doc = new Document { Id = 2, Title = "To Delete", FilePath = $"/uploads/{fileName}", AuthorId = "user-1" };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, mockEnv.Object);
            SetupControllerContext(controller);

            var methodInfo = typeof(DocumentController).GetMethod("DeleteConfirmed") ?? typeof(DocumentController).GetMethod("Delete");
            if (methodInfo != null)
            {
                await (Task<IActionResult>)methodInfo.Invoke(controller, new object[] { 2 });
            }

            Assert.Null(db.Documents.Find(2)); // Видалено з БД
            Assert.False(File.Exists(physicalPath), "Файл залишився на диску після видалення документа!"); // Видалено з диску
        }

        [Fact]
        public async Task TC_I204_DownloadFile_ReturnsPhysicalFile()
        {
            using var db = GetInMemoryDbContext();
            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns(_tempWebRoot);

            var uploadsFolder = Path.Combine(_tempWebRoot, "uploads");
            Directory.CreateDirectory(uploadsFolder);
            File.WriteAllText(Path.Combine(uploadsFolder, "test.pdf"), "content");

            db.Documents.Add(new Document { Id = 3, FilePath = "/uploads/test.pdf", OriginalFileName = "Oryginal.pdf" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, mockEnv.Object);
            SetupControllerContext(controller);

            var result = await controller.Download(3) as PhysicalFileResult;

            Assert.NotNull(result);
            Assert.Equal("Oryginal.pdf", result.FileDownloadName);
            Assert.True(File.Exists(result.FileName));
        }

        #endregion

        #region TC_I301 - TC_I304: Аудит станів

        [Fact]
        public async Task TC_I301_CreateDocument_CreatesAuditLogWithNullOldStatus()
        {
            using var db = GetInMemoryDbContext();

            // ИСПРАВЛЕНИЕ: Настраиваем WebRootPath, чтобы Path.Combine не получал null
            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns(_tempWebRoot);

            var controller = new DocumentController(db, GetMockUserManager().Object, mockEnv.Object);
            SetupControllerContext(controller);

            await controller.Create(new Document { Title = "Audit Test" }, GetMockFile("a.pdf").Object);

            var audit = db.DocumentHistories.FirstOrDefault();
            Assert.NotNull(audit);
            Assert.Null(audit.OldStatus); // OldStatus має бути null
            Assert.Equal(DocumentStatus.Draft, audit.NewStatus); // NewStatus має бути Draft
        }

        [Fact]
        public async Task TC_I302_AuthorSendsToReview_CreatesAuditLog()
        {
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 4, Title = "Doc", Status = DocumentStatus.Draft, AuthorId = "user-1" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller);

            await controller.ChangeStatus(4, DocumentStatus.PendingReview);

            var audit = db.DocumentHistories.LastOrDefault();
            Assert.Equal(DocumentStatus.Draft, audit.OldStatus);
            Assert.Equal(DocumentStatus.PendingReview, audit.NewStatus);
        }

        [Fact]
        public async Task TC_I303_ManagerApproves_CreatesAuditLog()
        {
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 5, Title = "Doc", Status = DocumentStatus.PendingReview, AuthorId = "user-1" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller, "Менеджер");

            await controller.ChangeStatus(5, DocumentStatus.Approved);

            var audit = db.DocumentHistories.LastOrDefault();
            Assert.Equal(DocumentStatus.PendingReview, audit.OldStatus);
            Assert.Equal(DocumentStatus.Approved, audit.NewStatus);
        }

        [Fact]
        public async Task TC_I304_DeleteDocument_KeepsAuditLogWithSetNull()
        {
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 6, Title = "To Delete Audit", AuthorId = "user-1" });
            db.DocumentHistories.Add(new DocumentHistory { Id = 10, DocumentId = 6, DocumentTitle = "To Delete Audit" });
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);

            var methodInfo = typeof(DocumentController).GetMethod("DeleteConfirmed") ?? typeof(DocumentController).GetMethod("Delete");
            if (methodInfo != null)
            {
                SetupControllerContext(controller, "Адміністратор");
                await (Task<IActionResult>)methodInfo.Invoke(controller, new object[] { 6 });
            }

            Assert.Null(db.Documents.Find(6));
            var retainedAudit = db.DocumentHistories.Find(10);
            Assert.NotNull(retainedAudit);
            Assert.Equal("To Delete Audit", retainedAudit.DocumentTitle); // Назва збереглася!
        }

        #endregion

        #region TC_I401 - TC_I402: Адміністрування та Профіль

        [Fact]
        public async Task TC_I401_AssignAndRemoveRoles_UpdatesDatabaseLinks()
        {
            var mockUserManager = GetMockUserManager();
            var user = new ApplicationUser { Id = "user-1" };
            mockUserManager.Setup(x => x.FindByIdAsync("user-1")).ReturnsAsync(user);
            mockUserManager.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Співробітник" });

            // ИСПРАВЛЕНИЕ: Говорим менеджеру возвращать "Успех" при удалении и добавлении ролей
            mockUserManager.Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
            mockUserManager.Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);

            var controller = new AdminController(mockUserManager.Object, null, GetInMemoryDbContext());
            SetupControllerContext(controller);

            var model = new List<ManageUserRolesViewModel>
    {
        new ManageUserRolesViewModel { RoleName = "Менеджер", IsSelected = true },
        new ManageUserRolesViewModel { RoleName = "Співробітник", IsSelected = false } // Знімаємо роль
    };

            await controller.ManageRoles(model, "user-1");

            // Перевіряємо інтеграцію методів: видалення старої та призначення нової ролі
            mockUserManager.Verify(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()), Times.Once);
            mockUserManager.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Contains("Менеджер"))), Times.Once);
        }

        [Fact]
        public async Task TC_I402_UpdateProfile_SavesAvatarToDiskAndChangesPassword()
        {
            var mockUserManager = GetMockUserManager();
            var user = new ApplicationUser { Id = "user-1" };
            mockUserManager.Setup(x => x.GetUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);
            mockUserManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);
            mockUserManager.Setup(x => x.ChangePasswordAsync(user, "Old", "New")).ReturnsAsync(IdentityResult.Success);

            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns(_tempWebRoot);

            var controller = new ProfileController(mockUserManager.Object, GetInMemoryDbContext(), mockEnv.Object);
            SetupControllerContext(controller);

            var avatarFile = GetMockFile("myface.png");
            var model = new UserProfileViewModel { FirstName = "Ivan", CurrentPassword = "Old", NewPassword = "New", AvatarFile = avatarFile.Object };

            await controller.Edit(model);

            // Перевіряємо запис в БД та зміну пароля
            Assert.Equal("Ivan", user.FirstName);
            Assert.NotNull(user.AvatarPath);
            mockUserManager.Verify(x => x.ChangePasswordAsync(user, "Old", "New"), Times.Once);

            // Перевіряємо фізичний запис файлу аватара на диск
            var physicalAvatarPath = Path.Combine(_tempWebRoot, user.AvatarPath.TrimStart('/'));
            Assert.True(File.Exists(physicalAvatarPath), "Файл аватара не був збережений на диску!");
        }

        #endregion
    }
}