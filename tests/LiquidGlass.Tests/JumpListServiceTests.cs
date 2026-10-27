using LiquidGlass.Models;
using LiquidGlass.Services;

namespace LiquidGlass.Tests;

public class JumpListServiceTests
{
    private static readonly string AppId = "LiquidGlass.Tests." + Guid.NewGuid().ToString("N");

    private static readonly Lock WriterLock = new();

    public static TheoryData<byte[]> InvalidFiles => new()
    {
        Array.Empty<byte>(),
        new byte[] { 1, 2, 3 },

        // Um grupo cujo nome diz ter 65535 caracteres num arquivo de 18 bytes.
        new byte[] { 2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xFF, 0xFF },

        // Contagens gigantes de grupos e de itens.
        new byte[] { 2, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F, 0, 0, 0, 0, 2, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0x7F },

        // Tipo de grupo desconhecido.
        new byte[] { 2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 9, 0, 0, 0 },

        // Um item de uma classe COM que não é atalho nem item do shell.
        new byte[] { 2, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0 }.Concat(Guid.NewGuid().ToByteArray()).ToArray(),
    };

    private static string ListPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        @"Microsoft\Windows\Recent\CustomDestinations",
        JumpListService.FileNameFor(AppId));

    [Fact]
    public void FileNameMatchesTheOneWindowsUsesForVisualStudioCode()
    {
        Assert.Equal("1ced32d74a95c7bc.customDestinations-ms", JumpListService.FileNameFor("Microsoft.VisualStudioCode"));
    }

    [Fact]
    public void FileNameIgnoresCase()
    {
        Assert.Equal(JumpListService.FileNameFor("Microsoft.VisualStudioCode"), JumpListService.FileNameFor("MICROSOFT.visualstudiocode"));
    }

    [Theory]
    [MemberData(nameof(InvalidFiles))]
    public void InvalidFilesGiveAnEmptyList(byte[] data)
    {
        Sta.Run(() => Assert.Empty(JumpListService.Parse(data)));
    }

    [Fact]
    public void LoadWithoutWindowOrShortcutIsEmpty()
    {
        Assert.Empty(JumpListService.Load(null, null));
    }

    [Fact]
    public void ReadsTheListWindowsWrote()
    {
        WithTestList(path =>
        {
            var categories = LoadEventually();

            var group = Assert.Single(categories, category => !category.IsTasks);
            Assert.Equal("Grupo de teste", group.Name);
            Assert.Equal(["Item 1", "Item 2", "Item 3", "Item 4", "Item 5", "Item 6"], group.Entries.Select(entry => entry.Title));

            // As divisórias das pontas somem; a do meio fica.
            var tasks = Assert.Single(categories, category => category.IsTasks);
            Assert.Collection(
                tasks.Entries,
                entry => Assert.Equal("Nova janela", entry.Title),
                entry => Assert.True(entry.IsSeparator),
                entry => Assert.Equal("Nova janela anônima", entry.Title));
            Assert.All(tasks.Entries.Where(entry => !entry.IsSeparator), entry => Assert.False(entry.Link.IsEmpty));
        });
    }

    [Fact]
    public void TruncatedAndCorruptedListsNeverThrow()
    {
        WithTestList(path =>
        {
            var original = File.ReadAllBytes(path);
            for (var length = 0; length < original.Length; length++)
            {
                JumpListService.Parse(original[..length]);
            }

            var random = new Random(42);
            for (var attempt = 0; attempt < 1000; attempt++)
            {
                var data = (byte[])original.Clone();
                for (var change = 0; change < 8; change++)
                {
                    data[random.Next(data.Length)] = (byte)random.Next(256);
                }

                JumpListService.Parse(data);
            }
        });
    }

    [Fact]
    public void LaunchReportsAFileThatNoLongerExists()
    {
        var missing = new JumpListEntry("Pasta apagada", ReadOnlyMemory<byte>.Empty, @"C:\LiquidGlass-nao-existe\arquivo.txt");

        Assert.False(JumpListService.Launch(missing));
    }

    [Fact]
    public void LaunchReportsAShortcutToAProgramThatNoLongerExists()
    {
        WithTestList(
            _ =>
            {
                var task = LoadEventually().Single(category => category.IsTasks).Entries.First(entry => !entry.IsSeparator);

                Assert.False(JumpListService.Launch(task));
            },
            target: @"C:\LiquidGlass-nao-existe\programa.exe");
    }

    [Fact]
    public void StaleTemporaryLinksAreDeletedAndRecentOnesKept()
    {
        Directory.CreateDirectory(JumpListService.TemporaryFolder);
        var stale = Path.Combine(JumpListService.TemporaryFolder, $"jumplist-teste-{Guid.NewGuid():N}.lnk");
        var recent = Path.Combine(JumpListService.TemporaryFolder, $"jumplist-teste-{Guid.NewGuid():N}.lnk");
        File.WriteAllBytes(stale, []);
        File.WriteAllBytes(recent, []);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddMinutes(-5));
        try
        {
            JumpListService.DeleteStaleLinks();

            Assert.False(File.Exists(stale));
            Assert.True(File.Exists(recent));
        }
        finally
        {
            File.Delete(stale);
            File.Delete(recent);
        }
    }

    /// <summary>Lê como o menu, que desiste depois de um instante e acha a lista pronta na vez seguinte.</summary>
    private static IReadOnlyList<JumpListCategory> LoadEventually()
    {
        var pin = new PinnedApp("teste", "Teste", null, AppId);
        var deadline = DateTime.UtcNow.AddSeconds(10);
        IReadOnlyList<JumpListCategory> categories;
        do
        {
            categories = JumpListService.Load(null, pin);
        }
        while (categories.Count == 0 && DateTime.UtcNow < deadline);

        return categories;
    }

    private static void WithTestList(Action<string> test, string target = JumpListWriter.Notepad)
    {
        lock (WriterLock)
        {
            Sta.Run(() =>
            {
                try
                {
                    JumpListWriter.Write(
                        AppId,
                        [null, "Nova janela", null, "Nova janela anônima", null],
                        "Grupo de teste",
                        [.. Enumerable.Range(1, 8).Select(number => $"Item {number}")],
                        target);
                    Assert.True(File.Exists(ListPath), "O Windows não gravou a lista de teste.");
                    test(ListPath);
                }
                finally
                {
                    JumpListWriter.Delete(AppId);
                }
            });
        }
    }
}
