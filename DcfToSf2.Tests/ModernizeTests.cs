using System.Text;
using DcfToSf2.Diagnostics;
using DcfToSf2.Layout;
using DcfToSf2.Writing;
using Xunit;

namespace DcfToSf2.Tests;

public class ModernizeTests
{
    private static readonly Encoding Cp1251;

    static ModernizeTests()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    private sealed class CollectingLog : IConversionLog
    {
        public List<(ConversionLogLevel Level, string Message)> Entries { get; } = [];

        public void Info(string message) => Entries.Add((ConversionLogLevel.Info, message));

        public void Success(string message) => Entries.Add((ConversionLogLevel.Success, message));

        public void Warning(string message) => Entries.Add((ConversionLogLevel.Warning, message));

        public void Error(string message) => Entries.Add((ConversionLogLevel.Error, message));

        public bool Has(string substring, ConversionLogLevel? level = null) =>
            Entries.Any(e =>
                e.Message.Contains(substring, StringComparison.OrdinalIgnoreCase)
                && (level is null || e.Level == level)
            );
    }

    private static string WriteExistingSf2(DirectoryInfo dir, string documentName, string body)
    {
        string path = Path.Combine(dir.FullName, $"{documentName}.sf2");
        File.WriteAllText(path, body, Cp1251);
        return path;
    }

    private static DirectoryInfo TempDir()
    {
        var dir = new DirectoryInfo(
            Path.Combine(Path.GetTempPath(), "dcf2sf2_modernize_" + Guid.NewGuid().ToString("N"))
        );
        dir.Create();
        return dir;
    }

    private static void TryDelete(DirectoryInfo dir)
    {
        try
        {
            dir.Delete(true);
        }
        catch
        {
            /* ignore */
        }
    }

    [Fact]
    public void Sf2Reader_ParsesSectionsAndDataNames()
    {
        var dir = TempDir();
        try
        {
            string path = WriteExistingSf2(
                dir,
                "Doc",
                """
                DOCUMENT=Doc
                [MAIN]
                778x80 0 0 `Title
                Data          10    20     6    5 OldField `old
                /
                [Goods]
                778x60 0 0 `Goods %d
                Data          10    20     6    5 Goods_Name `n
                /
                """
            );

            var file = Sf2Reader.Read(path);
            Assert.Equal("Doc", file.DocumentName);
            Assert.Contains("MAIN", file.SectionNames);
            Assert.Contains("Goods", file.SectionNames);
            Assert.Contains("OldField", file.Sections[0].DataFieldNames);
            Assert.Contains("Goods_Name", file.Sections[1].DataFieldNames);
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Modernize_AppendsNewBlock_PreservesExisting_WarnsAboutParentLink()
    {
        var dir = TempDir();
        try
        {
            const string marker = "EXISTING_MARKER_DO_NOT_TOUCH";
            string original =
                $"""
                DOCUMENT=Doc
                [MAIN]
                778x80 0 0 `Title
                Data          10    20     6    5 RootField `{marker}
                /
                [Goods]
                778x60 0 0 `Goods %d
                Data          10    20     6    5 Goods_Name `n
                /
                """;
            string path = WriteExistingSf2(dir, "Doc", original);
            string before = File.ReadAllText(path, Cp1251);

            var fresh = new Sf2Document { DocumentName = "Doc" };
            fresh.Blocks.Add(
                new Block
                {
                    Name = "MAIN",
                    Title = "Title",
                    Fields =
                    [
                        new Field(
                            Type.Data,
                            new Size(6, 5),
                            new Position(10, 20),
                            new Value("RootField", "x")
                        ),
                    ],
                }
            );
            fresh.Blocks.Add(
                new Block
                {
                    Name = "Goods",
                    Title = "Goods",
                    IsRepeating = true,
                    Fields =
                    [
                        new Field(
                            Type.Data,
                            new Size(6, 5),
                            new Position(10, 20),
                            new Value("Goods_Name", "n")
                        ),
                    ],
                }
            );
            fresh.Blocks.Add(
                new Block
                {
                    Name = @"Goods\NewThing",
                    Title = "New thing",
                    Fields =
                    [
                        new Field(
                            Type.Data,
                            new Size(6, 5),
                            new Position(10, 20),
                            new Value("NewThing_Code", "c")
                        ),
                    ],
                }
            );

            var log = new CollectingLog();
            int code = Sf2Modernizer.Apply(dir, fresh, log);
            Assert.Equal(0, code);

            string after = File.ReadAllText(path, Cp1251);
            string beforeNorm = before.Replace("\r\n", "\n").TrimEnd('\n');
            string afterNorm = after.Replace("\r\n", "\n");
            Assert.StartsWith(beforeNorm, afterNorm, StringComparison.Ordinal);
            Assert.Contains(marker, after);
            Assert.Contains("[Goods\\NewThing]", after);
            Assert.Contains("NewThing_Code", after);
            Assert.True(log.Has("Added block: [Goods\\NewThing]", ConversionLogLevel.Success));
            Assert.True(log.Has("Skipped existing: [MAIN]", ConversionLogLevel.Info));
            Assert.True(log.Has("Skipped existing: [Goods]", ConversionLogLevel.Info));
            Assert.True(log.Has("@NewThing@", ConversionLogLevel.Warning));
            Assert.True(log.Has("[Goods]", ConversionLogLevel.Warning));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Modernize_NoNewBlocks_DoesNotChangeFile()
    {
        var dir = TempDir();
        try
        {
            string original =
                """
                DOCUMENT=Doc
                [MAIN]
                778x40 0 0 `Title
                /
                """;
            string path = WriteExistingSf2(dir, "Doc", original);
            byte[] before = File.ReadAllBytes(path);

            var fresh = new Sf2Document { DocumentName = "Doc" };
            fresh.Blocks.Add(new Block { Name = "MAIN", Title = "Title" });

            var log = new CollectingLog();
            int code = Sf2Modernizer.Apply(dir, fresh, log);
            Assert.Equal(0, code);
            Assert.Equal(before, File.ReadAllBytes(path));
            Assert.True(log.Has("Изменений нет", ConversionLogLevel.Info));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Modernize_MissingSf2_ReturnsError()
    {
        var dir = TempDir();
        try
        {
            var fresh = new Sf2Document { DocumentName = "MissingDoc" };
            fresh.Blocks.Add(new Block { Name = "MAIN", Title = "T" });

            var log = new CollectingLog();
            int code = Sf2Modernizer.Apply(dir, fresh, log);
            Assert.Equal(1, code);
            Assert.True(log.Has("не найден", ConversionLogLevel.Error));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void Modernize_LogsSkippedNewFieldsInExistingBlock()
    {
        var dir = TempDir();
        try
        {
            WriteExistingSf2(
                dir,
                "Doc",
                """
                DOCUMENT=Doc
                [MAIN]
                778x80 0 0 `Title
                Data          10    20     6    5 OldField `old
                /
                """
            );

            var fresh = new Sf2Document { DocumentName = "Doc" };
            fresh.Blocks.Add(
                new Block
                {
                    Name = "MAIN",
                    Title = "Title",
                    Fields =
                    [
                        new Field(
                            Type.Data,
                            new Size(6, 5),
                            new Position(10, 20),
                            new Value("OldField", "old")
                        ),
                        new Field(
                            Type.Data,
                            new Size(6, 5),
                            new Position(10, 40),
                            new Value("BrandNewField", "new")
                        ),
                    ],
                }
            );

            var log = new CollectingLog();
            int code = Sf2Modernizer.Apply(dir, fresh, log);
            Assert.Equal(0, code);
            Assert.True(log.Has("Изменений нет", ConversionLogLevel.Info));
            Assert.True(log.Has("BrandNewField", ConversionLogLevel.Warning));
            Assert.True(log.Has("Skipped new fields in existing block [MAIN]", ConversionLogLevel.Warning));
        }
        finally
        {
            TryDelete(dir);
        }
    }

    [Fact]
    public void ConvertFile_Modernize_MissingSf2_ReturnsError()
    {
        var dir = TempDir();
        try
        {
            string dcfPath = Path.Combine(dir.FullName, "TinyDoc.Dcf");
            string dcf =
                """
                [TinyDoc.Header]
                ShortName=Tiny
                FullName=Tiny Doc

                [TinyDoc.Fields]
                RootField=C 20
                """;
            File.WriteAllText(dcfPath, dcf, Cp1251);

            var options = new ConversionOptions
            {
                InputFile = new FileInfo(dcfPath),
                OutputDirectory = dir,
                Modernize = true,
                IgnoreFields = [],
            };

            var log = new CollectingLog();
            int code = MainOperations.MainOperations.ConvertFile(options, log, writePreview: false);
            Assert.Equal(1, code);
            Assert.True(log.Has("не найден", ConversionLogLevel.Error));
            Assert.False(File.Exists(Path.Combine(dir.FullName, "TinyDoc.sf2")));
        }
        finally
        {
            TryDelete(dir);
        }
    }
}
