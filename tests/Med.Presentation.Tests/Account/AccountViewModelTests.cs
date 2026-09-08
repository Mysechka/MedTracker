using FluentAssertions;
using Med.Application.Abstractions;
using Med.Application.UseCases;
using Med.Domain.Entities;
using Med.Domain.ValueObjects;
using Med.Presentation.Abstractions;
using Med.Presentation.Account;
using Med.Presentation.Feedback;
using Med.Presentation.Messaging;
using Xunit;

namespace Med.Presentation.Tests.Account;

public sealed class AccountViewModelTests
{
    private static readonly Guid TestUserId = Guid.Parse("99999999-9999-9999-9999-999999999999");

    [Fact]
    public async Task Refresh_загружает_имя_пользователя_и_считает_инициал()
    {
        Profile profile = CreateProfile("Александр");
        FakeProfileRepo repo = new(profile);
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Username.Should().Be("Александр");
        vm.AvatarInitial.Should().Be("А");
        vm.HasUsernameError.Should().BeFalse();
    }

    [Fact]
    public async Task Save_с_валидными_данными_обновляет_профиль_и_пароль()
    {
        Profile profile = CreateProfile("СтароеИмя");
        FakeProfileRepo repo = new(profile);
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Username = "НовоеИмя";
        vm.Codeword = "newSecret123";

        await vm.SaveCommand.ExecuteAsync(null);

        repo.UpdatedProfile.Should().NotBeNull();
        repo.UpdatedProfile!.Username.Should().Be("НовоеИмя");
        auth.LastUpdatedPassword.Should().Be("newSecret123");
        vm.Codeword.Should().BeEmpty();
        vm.HasUsernameError.Should().BeFalse();
        vm.HasCodewordError.Should().BeFalse();
    }

    [Fact]
    public async Task Save_с_пустым_именем_выставляет_ошибку_и_не_сохраняет()
    {
        Profile profile = CreateProfile("Иван");
        FakeProfileRepo repo = new(profile);
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Username = "";

        await vm.SaveCommand.ExecuteAsync(null);

        vm.HasUsernameError.Should().BeTrue();
        repo.UpdatedProfile.Should().BeNull();
    }

    [Fact]
    public async Task Save_с_коротким_паролем_выставляет_ошибку()
    {
        Profile profile = CreateProfile("Иван");
        FakeProfileRepo repo = new(profile);
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.RefreshCommand.ExecuteAsync(null);

        vm.Username = "Иван";
        vm.Codeword = "123";

        await vm.SaveCommand.ExecuteAsync(null);

        vm.HasCodewordError.Should().BeTrue();
        auth.LastUpdatedPassword.Should().BeNull();
    }

    [Fact]
    public async Task SignOut_вызывает_сервис_аутентификации()
    {
        FakeProfileRepo repo = new(CreateProfile("Иван"));
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        await vm.SignOutCommand.ExecuteAsync(null);

        auth.SignOutCalled.Should().BeTrue();
    }

    [Fact]
    public async Task ChangeAvatar_с_выбранным_файлом_открывает_режим_кадрирования()
    {
        string tempFile = Path.GetTempFileName();
        try
        {
            FakeProfileRepo repo = new(CreateProfile("Иван"));
            FakeAuthService auth = new(TestUserId);
            FakeFilePickerService filePicker = new(tempFile);
            FakeCropService cropService = new();
            AccountViewModel vm = CreateViewModel(repo, auth, filePicker, cropService);

            await vm.ChangeAvatarCommand.ExecuteAsync(null);

            vm.IsCropping.Should().BeTrue();
            vm.CropSourcePath.Should().Be(tempFile);
            vm.CropZoom.Should().Be(1.0);
            vm.CropPanX.Should().Be(0);
            vm.CropPanY.Should().Be(0);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    [Fact]
    public async Task ChangeAvatar_без_файла_не_включает_кадрирование()
    {
        FakeProfileRepo repo = new(CreateProfile("Иван"));
        FakeAuthService auth = new(TestUserId);
        FakeFilePickerService filePicker = new(null);
        AccountViewModel vm = CreateViewModel(repo, auth, filePicker);

        await vm.ChangeAvatarCommand.ExecuteAsync(null);

        vm.IsCropping.Should().BeFalse();
        vm.CropSourcePath.Should().BeNull();
    }

    [Fact]
    public async Task ApplyCrop_вызывает_сервис_кадрирования_и_сохраняет_аватар()
    {
        string tempSource = Path.GetTempFileName();
        string tempResult = Path.GetTempFileName();
        try
        {
            FakeProfileRepo repo = new(CreateProfile("Иван"));
            FakeAuthService auth = new(TestUserId);
            FakeFilePickerService filePicker = new(tempSource);
            FakeCropService cropService = new(tempResult);
            AccountViewModel vm = CreateViewModel(repo, auth, filePicker, cropService);

            await vm.ChangeAvatarCommand.ExecuteAsync(null);

            vm.CropZoom = 1.8;
            vm.PanCrop(15.5, -20.0);

            await vm.ApplyCropCommand.ExecuteAsync(null);

            cropService.LastSourcePath.Should().Be(tempSource);
            cropService.LastUserId.Should().Be(TestUserId);
            cropService.LastZoom.Should().Be(1.8);
            cropService.LastPanX.Should().Be(15.5);
            cropService.LastPanY.Should().Be(-20.0);

            vm.IsCropping.Should().BeFalse();
            vm.CropSourcePath.Should().BeNull();
            vm.AvatarPath.Should().StartWith(tempResult);
            vm.AvatarPath.Should().Contain("?v=");
            vm.HasAvatar.Should().BeTrue();
        }
        finally
        {
            if (File.Exists(tempSource)) File.Delete(tempSource);
            if (File.Exists(tempResult)) File.Delete(tempResult);
        }
    }

    [Fact]
    public void CancelCrop_закрывает_режим_кадрирования_и_сбрасывает_состояние()
    {
        FakeProfileRepo repo = new(CreateProfile("Иван"));
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        vm.IsCropping = true;
        vm.CropSourcePath = "/some/path.png";
        vm.CropZoom = 2.5;
        vm.PanCrop(30, 40);

        vm.CancelCropCommand.Execute(null);

        vm.IsCropping.Should().BeFalse();
        vm.CropSourcePath.Should().BeNull();
        vm.CropZoom.Should().Be(1.0);
        vm.CropPanX.Should().Be(0);
        vm.CropPanY.Should().Be(0);
    }

    [Fact]
    public void ResetCropPosition_сбрасывает_масштаб_и_смещение()
    {
        FakeProfileRepo repo = new(CreateProfile("Иван"));
        FakeAuthService auth = new(TestUserId);
        AccountViewModel vm = CreateViewModel(repo, auth);

        vm.CropZoom = 2.8;
        vm.PanCrop(50, -50);

        vm.ResetCropPositionCommand.Execute(null);

        vm.CropZoom.Should().Be(1.0);
        vm.CropPanX.Should().Be(0);
        vm.CropPanY.Should().Be(0);
    }

    private static Profile CreateProfile(string username) =>
        Profile.Create(
            TestUserId,
            username,
            "Europe/Moscow",
            new MealWindows(new TimeOnly(8, 0), new TimeOnly(13, 0), new TimeOnly(19, 0)));

    private static AccountViewModel CreateViewModel(
        IProfileRepository repo,
        IAuthService auth,
        IFilePickerService? filePicker = null,
        IImageCropService? cropService = null)
    {
        UpdateProfileUseCase useCase = new(repo);
        return new AccountViewModel(
            repo,
            auth,
            useCase,
            filePicker ?? new NullFilePickerService(),
            TestFeedback.Instance,
            cropService ?? new NullImageCropService());
    }

    private sealed class FakeProfileRepo(Profile profile) : IProfileRepository
    {
        public Profile? UpdatedProfile { get; private set; }

        public Task<Profile?> GetCurrentAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<Profile?>(UpdatedProfile ?? profile);

        public Task UpdateAsync(Profile newProfile, CancellationToken cancellationToken = default)
        {
            UpdatedProfile = newProfile;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuthService(Guid userId) : IAuthService
    {
        public string? LastUpdatedPassword { get; private set; }
        public bool SignOutCalled { get; private set; }

        public AuthSession? CurrentSession => new(userId, "test@medtracker.local", "token", "refresh", DateTimeOffset.UtcNow.AddHours(1));
        public Guid? CurrentUserId => userId;

        public event EventHandler<AuthSession?>? AuthStateChanged
        {
            add { }
            remove { }
        }

        public Task<AuthSession> SignInWithPasswordAsync(string email, string password, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AuthSession> SignUpWithPasswordAsync(string email, string password, string? username = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SendMagicLinkAsync(string email, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task UpdatePasswordAsync(string newPassword, CancellationToken cancellationToken = default)
        {
            LastUpdatedPassword = newPassword;
            return Task.CompletedTask;
        }

        public Task SignOutAsync(CancellationToken cancellationToken = default)
        {
            SignOutCalled = true;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeFilePickerService(string? resultPath) : IFilePickerService
    {
        public Task<string?> PickImageFileAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(resultPath);
    }

    private sealed class FakeCropService(string? resultPath = null) : IImageCropService
    {
        public string? LastSourcePath { get; private set; }
        public Guid LastUserId { get; private set; }
        public double LastZoom { get; private set; }
        public double LastPanX { get; private set; }
        public double LastPanY { get; private set; }

        public Task<string> CropAndSaveAvatarAsync(
            string sourceImagePath,
            Guid userId,
            double zoom,
            double panX,
            double panY,
            int targetSize = 256,
            CancellationToken cancellationToken = default)
        {
            LastSourcePath = sourceImagePath;
            LastUserId = userId;
            LastZoom = zoom;
            LastPanX = panX;
            LastPanY = panY;
            return Task.FromResult(resultPath ?? sourceImagePath);
        }
    }
}
