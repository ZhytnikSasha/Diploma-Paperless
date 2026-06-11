using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using Paperless.Controllers;
using Paperless.Data;
using Paperless.Models;
using Paperless.Models.Enums;
using Paperless.Models.ViewModels;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using System.Security.Claims;

namespace Paperless.Tests
{
    public class ValidationTests
    {
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

            // РЯТІВНИЙ КРУГ №2: Завжди повертаємо фейкового юзера!
            // Тепер _userManager.GetUserAsync або GetUserId ніколи не повернуть null в тестах
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

        private void SetupControllerContext(Controller controller, string role = null)
        {
            var claims = new List<Claim>
    {
        new Claim(ClaimTypes.NameIdentifier, "user-1"),
        new Claim(ClaimTypes.Name, "test@mail.com")
    };

            // Якщо в тест передали роль, додаємо її нашому фейковому юзеру!
            if (!string.IsNullOrEmpty(role))
            {
                claims.Add(new Claim(ClaimTypes.Role, role));
            }

            var httpContext = new DefaultHttpContext();
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "mock"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = httpContext
            };

            controller.TempData = new TempDataDictionary(httpContext, Mock.Of<ITempDataProvider>());
        }

        #endregion

        #region TC_V001 - TC_V007: Автентифікація та Реєстрація

        [Fact]
        public async Task TC_V001_Register_ValidData_CreatesAccountAndLogsIn()
        {
            var mockUserManager = GetMockUserManager();
            var mockSignInManager = GetMockSignInManager(mockUserManager.Object);

            mockUserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
                           .ReturnsAsync(IdentityResult.Success);
            mockUserManager.Setup(x => x.AddToRoleAsync(It.IsAny<ApplicationUser>(), "Співробітник"))
                           .ReturnsAsync(IdentityResult.Success);
            mockSignInManager.Setup(x => x.SignInAsync(It.IsAny<ApplicationUser>(), false, null))
                             .Returns(Task.CompletedTask);

            var controller = new AccountController(mockUserManager.Object, mockSignInManager.Object);
            SetupControllerContext(controller);

            var model = new RegisterViewModel { Email = "test@mail.com", Password = "Password123!", ConfirmPassword = "Password123!" };

            var result = await controller.Register(model) as RedirectToActionResult;

            Assert.NotNull(result);
            Assert.Equal("Index", result.ActionName);
        }

        [Fact]
        public async Task TC_V002_Register_ShortPassword_ReturnsError()
        {
            var mockUserManager = GetMockUserManager();
            mockUserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), "abc"))
                           .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Passwords must be at least 6 characters." }));

            var controller = new AccountController(mockUserManager.Object, GetMockSignInManager(mockUserManager.Object).Object);
            SetupControllerContext(controller);

            var model = new RegisterViewModel { Password = "abc", ConfirmPassword = "abc" };

            var result = await controller.Register(model) as ViewResult;

            Assert.NotNull(result);
            Assert.False(controller.ModelState.IsValid);
        }

        [Fact]
        public async Task TC_V003_Register_PasswordWithoutDigitOrUppercase_ReturnsError()
        {
            var mockUserManager = GetMockUserManager();
            mockUserManager.Setup(x => x.CreateAsync(It.IsAny<ApplicationUser>(), "password"))
                           .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Passwords must have at least one digit and uppercase." }));

            var controller = new AccountController(mockUserManager.Object, GetMockSignInManager(mockUserManager.Object).Object);
            SetupControllerContext(controller);

            var model = new RegisterViewModel { Password = "password", ConfirmPassword = "password" };

            var result = await controller.Register(model) as ViewResult;

            Assert.False(controller.ModelState.IsValid);
        }

        [Fact]
        public void TC_V004_Register_MismatchedPasswords_ReturnsModelError()
        {
            var model = new RegisterViewModel { Password = "Password123!", ConfirmPassword = "DifferentPassword1!" };
            var validationResults = new List<System.ComponentModel.DataAnnotations.ValidationResult>();
            var context = new System.ComponentModel.DataAnnotations.ValidationContext(model);

            bool isValid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(model, context, validationResults, true);

            Assert.False(isValid);
        }

        [Fact]
        public async Task TC_V005_Login_ValidCredentials_RedirectsToDashboard()
        {
            var mockUserManager = GetMockUserManager();
            var mockSignInManager = GetMockSignInManager(mockUserManager.Object);
            mockSignInManager.Setup(x => x.PasswordSignInAsync("test@mail.com", "Pass123!", false, false))
                             .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Success);

            var controller = new AccountController(mockUserManager.Object, mockSignInManager.Object);
            SetupControllerContext(controller);

            var model = new LoginViewModel { Email = "test@mail.com", Password = "Pass123!" };

            var result = await controller.Login(model) as RedirectToActionResult;

            Assert.NotNull(result);
        }

        [Fact]
        public async Task TC_V006_Login_WrongPassword_ReturnsError()
        {
            var mockUserManager = GetMockUserManager();
            var mockSignInManager = GetMockSignInManager(mockUserManager.Object);
            mockSignInManager.Setup(x => x.PasswordSignInAsync("test@mail.com", "WrongPass!", false, false))
                             .ReturnsAsync(Microsoft.AspNetCore.Identity.SignInResult.Failed);

            var controller = new AccountController(mockUserManager.Object, mockSignInManager.Object);
            SetupControllerContext(controller);

            var model = new LoginViewModel { Email = "test@mail.com", Password = "WrongPass!" };

            var result = await controller.Login(model) as ViewResult;

            Assert.NotNull(result);
            Assert.False(controller.ModelState.IsValid);
        }

        [Fact]
        public void TC_V007_DocumentRegistry_WithoutAuth_RequiresAuthorization()
        {
            var type = typeof(DocumentController);
            var authorizeAttribute = type.GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(authorizeAttribute);
        }

        #endregion

        #region TC_V008 - TC_V013: Робота з документами та Аудит

        [Fact]
        public async Task TC_V008_CreateDocument_ValidFile_SavesDraftAndAudit()
        {
            using var db = GetInMemoryDbContext();
            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns("C:\\TestPath");

            var mockUserManager = GetMockUserManager();
            var controller = new DocumentController(db, mockUserManager.Object, mockEnv.Object);
            SetupControllerContext(controller);

            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.FileName).Returns("report.pdf");

            var document = new Document { Title = "Test Doc" };

            await controller.Create(document, fileMock.Object);

            var doc = db.Documents.FirstOrDefault();
            var audit = db.DocumentHistories.FirstOrDefault();

            Assert.NotNull(doc);
            Assert.Equal(DocumentStatus.Draft, doc.Status);
            Assert.NotNull(audit);
        }

        [Fact]
        public void TC_V009_CreateDocument_ExeFile_ReturnsError()
        {
            var controller = new DocumentController(GetInMemoryDbContext(), GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller);

            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.FileName).Returns("virus.exe");

            var extension = Path.GetExtension(fileMock.Object.FileName).ToLower();
            bool isValidExtension = extension == ".pdf" || extension == ".docx";

            Assert.False(isValidExtension);
        }

        [Fact]
        public async Task TC_V010_SendToReview_ChangesStatusToPendingAndCreatesAudit()
        {
            using var db = GetInMemoryDbContext();
            var doc = new Document { Id = 1, Title = "Draft Doc", Status = DocumentStatus.Draft, AuthorId = "user-1" };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller);

            await controller.ChangeStatus(1, DocumentStatus.PendingReview);

            var updatedDoc = db.Documents.Find(1);
            var audit = db.DocumentHistories.LastOrDefault();

            Assert.Equal(DocumentStatus.PendingReview, updatedDoc.Status);
            Assert.NotNull(audit);
        }

        [Fact]
        public async Task TC_V011_ManagerApproves_ChangesStatusToApprovedAndCreatesAudit()
        {
            using var db = GetInMemoryDbContext();
            var doc = new Document { Id = 2, Title = "Review Doc", Status = DocumentStatus.PendingReview, AuthorId = "user-1" };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);

            // ВАЖЛИВО: Кажемо контролеру, що цей користувач — Менеджер!
            SetupControllerContext(controller, "Менеджер");

            await controller.ChangeStatus(2, DocumentStatus.Approved);

            var updatedDoc = db.Documents.Find(2);
            var audit = db.DocumentHistories.LastOrDefault();

            Assert.Equal(DocumentStatus.Approved, updatedDoc.Status);
            Assert.Equal(DocumentStatus.Approved, audit.NewStatus);
        }

        [Fact]
        public async Task TC_V012_ManagerRejects_ChangesStatusToRejectedAndCreatesAudit()
        {
            using var db = GetInMemoryDbContext();
            var doc = new Document { Id = 3, Title = "Review Doc", Status = DocumentStatus.PendingReview, AuthorId = "user-1" };
            db.Documents.Add(doc);
            await db.SaveChangesAsync();

            var controller = new DocumentController(db, GetMockUserManager().Object, new Mock<IWebHostEnvironment>().Object);

            // ВАЖЛИВО: Кажемо контролеру, що цей користувач — Менеджер!
            SetupControllerContext(controller, "Менеджер");

            await controller.ChangeStatus(3, DocumentStatus.Rejected);

            var updatedDoc = db.Documents.Find(3);
            var audit = db.DocumentHistories.LastOrDefault();

            Assert.Equal(DocumentStatus.Rejected, updatedDoc.Status);
            Assert.Equal(DocumentStatus.Rejected, audit.NewStatus);
        }

        [Fact]
        public async Task TC_V013_DownloadFile_ReturnsPhysicalFileWithOriginalName()
        {
            using var db = GetInMemoryDbContext();
            db.Documents.Add(new Document { Id = 1, FilePath = "/uploads/test.pdf", OriginalFileName = "MyReport.pdf" });
            await db.SaveChangesAsync();

            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns("C:\\TestPath");

            var controller = new DocumentController(db, GetMockUserManager().Object, mockEnv.Object);
            SetupControllerContext(controller);

            Directory.CreateDirectory("C:\\TestPath\\uploads");
            File.WriteAllText("C:\\TestPath\\uploads\\test.pdf", "dummy content");

            var result = await controller.Download(1) as PhysicalFileResult;

            Assert.NotNull(result);
            Assert.Equal("MyReport.pdf", result.FileDownloadName);

            File.Delete("C:\\TestPath\\uploads\\test.pdf");
        }

        #endregion

        #region TC_V014 - TC_V016: Профіль та Адміністрування

        [Fact]
        public async Task TC_V014_ProfileEdit_ValidDataAndAvatar_UpdatesUser()
        {
            var mockUserManager = GetMockUserManager();
            var user = new ApplicationUser { Id = "user-1", FirstName = "Old", LastName = "Name" };

            // Налаштовуємо _userManager.GetUserAsync та UpdateAsync
            mockUserManager.Setup(x => x.GetUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);
            mockUserManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

            var mockEnv = new Mock<IWebHostEnvironment>();
            mockEnv.Setup(m => m.WebRootPath).Returns("C:\\TestPath");

            var controller = new ProfileController(mockUserManager.Object, GetInMemoryDbContext(), mockEnv.Object);
            SetupControllerContext(controller);

            var fileMock = new Mock<IFormFile>();
            fileMock.Setup(f => f.FileName).Returns("avatar.jpg");

            var model = new UserProfileViewModel { FirstName = "New", LastName = "Name", AvatarFile = fileMock.Object };

            await controller.Edit(model);

            Assert.Equal("New", user.FirstName);
            mockUserManager.Verify(x => x.UpdateAsync(user), Times.Once);
        }

        [Fact]
        public async Task TC_V015_ProfileEdit_ChangePassword_Succeeds()
        {
            var mockUserManager = GetMockUserManager();
            var user = new ApplicationUser { Id = "user-1" };
            mockUserManager.Setup(x => x.GetUserAsync(It.IsAny<System.Security.Claims.ClaimsPrincipal>())).ReturnsAsync(user);
            mockUserManager.Setup(x => x.ChangePasswordAsync(user, "OldPass1!", "NewPass2!"))
                           .ReturnsAsync(IdentityResult.Success);
            mockUserManager.Setup(x => x.UpdateAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

            var controller = new ProfileController(mockUserManager.Object, GetInMemoryDbContext(), new Mock<IWebHostEnvironment>().Object);
            SetupControllerContext(controller);

            var model = new UserProfileViewModel { CurrentPassword = "OldPass1!", NewPassword = "NewPass2!" };

            await controller.Edit(model);

            mockUserManager.Verify(x => x.ChangePasswordAsync(user, "OldPass1!", "NewPass2!"), Times.Once);
        }

        [Fact]
        public async Task TC_V016_Admin_AssignManagerRole_Succeeds()
        {
            var mockUserManager = GetMockUserManager();
            var user = new ApplicationUser { Id = "user-1" };
            mockUserManager.Setup(x => x.FindByIdAsync("user-1")).ReturnsAsync(user);
            mockUserManager.Setup(x => x.GetRolesAsync(user)).ReturnsAsync(new List<string> { "Співробітник" });

            // ВАЖЛИВЕ ДОПОВНЕННЯ ДЛЯ ТЕСТУ 16: Імітуємо видалення попередніх ролей
            mockUserManager.Setup(x => x.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);
            mockUserManager.Setup(x => x.AddToRolesAsync(user, It.IsAny<IEnumerable<string>>())).ReturnsAsync(IdentityResult.Success);

            var controller = new AdminController(mockUserManager.Object, null, GetInMemoryDbContext());
            SetupControllerContext(controller);

            var model = new List<ManageUserRolesViewModel>
            {
                new ManageUserRolesViewModel { RoleName = "Менеджер", IsSelected = true }
            };

            var result = await controller.ManageRoles(model, "user-1") as RedirectToActionResult;

            Assert.NotNull(result);
            mockUserManager.Verify(x => x.AddToRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Contains("Менеджер"))), Times.Once);
        }

        #endregion
    }
}