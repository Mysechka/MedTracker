using FluentAssertions;
using Med.Domain.Entities;
using Xunit;

namespace Med.Domain.Tests.Entities;

public sealed class DocumentTests
{
    [Theory]
    [InlineData("../../file.txt")]
    [InlineData("..\\file.txt")]
    [InlineData("sub/../../file.txt")]
    [InlineData("../")]
    [InlineData("..")]
    [InlineData("../../etc/passwd")]
    [InlineData("../avatars/target.png")]
    [InlineData("..\\..\\secret.pdf")]
    public void BuildStoragePath_при_попытке_path_traversal_выбрасывает_исключение(string unsafeFileName)
    {
        Guid userId = Guid.NewGuid();
        Guid docId = Guid.NewGuid();

        Action act = () => Document.BuildStoragePath(userId, docId, unsafeFileName);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildStoragePath_с_валидным_именем_файла_возвращает_безопасный_путь()
    {
        Guid userId = Guid.NewGuid();
        Guid docId = Guid.NewGuid();

        string path = Document.BuildStoragePath(userId, docId, "scan.pdf");

        path.Should().Be($"{userId}/{docId}/scan.pdf");
    }
}
