using FinQuery.Application.Auth;
using FinQuery.Domain.Errors;
using FinQuery.Domain.Users;

namespace FinQuery.Application.Tests;

public class AuthServiceTests
{
    [Fact]
    public async Task Register_StoresLowercaseEmail_AndDoesNotReturnTheHash()
    {
        var users = new FakeUsers();
        var service = Create(users, new FakeHasher(), new FakeTokens());

        var created = await service.RegisterAsync("  Casey@Example.com ", "StrongPassword123!", CancellationToken.None);

        Assert.Equal("casey@example.com", created.Email);
        Assert.True(created.IsActive);
        Assert.Equal("hashed:StrongPassword123!", users.Items[0].HashedPassword);
    }

    [Fact]
    public async Task Register_DuplicateEmail_IsConflict()
    {
        var users = new FakeUsers();
        var service = Create(users, new FakeHasher(), new FakeTokens());
        await service.RegisterAsync("casey@example.com", "StrongPassword123!", CancellationToken.None);

        var error = await Assert.ThrowsAsync<ConflictError>(() =>
            service.RegisterAsync("CASEY@example.com", "StrongPassword123!", CancellationToken.None));

        Assert.Equal(AuthService.DuplicateEmailMessage, error.Message);
    }

    [Theory]
    [InlineData("missing@example.com", "StrongPassword123!", false)]
    [InlineData("user@example.com", "wrong", true)]
    public async Task Login_Failure_UsesTheSameMessage(string email, string password, bool seed)
    {
        var users = new FakeUsers();
        var service = Create(users, new FakeHasher(), new FakeTokens());
        if (seed)
        {
            await service.RegisterAsync("user@example.com", "StrongPassword123!", CancellationToken.None);
        }

        var error = await Assert.ThrowsAsync<AuthenticationError>(() =>
            service.LoginAsync(email, password, CancellationToken.None));

        Assert.Equal(AuthService.InvalidLoginMessage, error.Message);
    }

    [Fact]
    public async Task Login_InactiveUser_UsesTheSameMessage()
    {
        var users = new FakeUsers();
        var service = Create(users, new FakeHasher(), new FakeTokens());
        await service.RegisterAsync("user@example.com", "StrongPassword123!", CancellationToken.None);
        users.Items[0].IsActive = false;

        var error = await Assert.ThrowsAsync<AuthenticationError>(() =>
            service.LoginAsync("user@example.com", "StrongPassword123!", CancellationToken.None));

        Assert.Equal(AuthService.InvalidLoginMessage, error.Message);
    }

    [Fact]
    public async Task Login_Success_PutsTheUserIdInTheTokenSubject()
    {
        var users = new FakeUsers();
        var tokens = new FakeTokens();
        var service = Create(users, new FakeHasher(), tokens);
        var created = await service.RegisterAsync("user@example.com", "StrongPassword123!", CancellationToken.None);

        var login = await service.LoginAsync("USER@example.com", "StrongPassword123!", CancellationToken.None);

        Assert.Equal("bearer", login.TokenType);
        Assert.Equal($"token:{created.Id}", login.AccessToken);
        Assert.Equal(created.Id, tokens.LastUserId);
    }

    private static AuthService Create(FakeUsers users, FakeHasher hasher, FakeTokens tokens) =>
        new(users, hasher, tokens);

    private sealed class FakeUsers : IUserRepository
    {
        public List<User> Items { get; } = [];

        public Task<User?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(user => user.Email == normalizedEmail));

        public Task<User?> FindByIdAsync(int id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(user => user.Id == id));

        public Task AddAsync(User user, CancellationToken cancellationToken)
        {
            user.Id = Items.Count + 1;
            Items.Add(user);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHasher : IPasswordHasher
    {
        public string Hash(string password) => "hashed:" + password;

        public bool Verify(string password, string hashedPassword) => hashedPassword == "hashed:" + password;
    }

    private sealed class FakeTokens : IAccessTokenIssuer
    {
        public int LastUserId { get; private set; }

        public string Create(int userId)
        {
            LastUserId = userId;
            return "token:" + userId;
        }
    }
}
