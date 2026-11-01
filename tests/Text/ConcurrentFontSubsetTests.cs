using System.Text.RegularExpressions;
using Aspose.Pdf.Tests.Helpers;
using Aspose.Pdf.Text;
using Xunit;

namespace Aspose.Pdf.Tests.Text;

/// <summary>Documents built and saved on parallel threads, each with its own document and its own
/// opened TrueType face: every saved file carries the same font subset as a document built alone.
/// A save cuts the subsets of every live embedded font, so it reaches fonts other threads are still
/// drawing into; a glyph drawn meanwhile must not be left out of a subset marked up to date.</summary>
public sealed class ConcurrentFontSubsetTests
{
    private const int Threads = 8;
    private const int Rounds = 100;

    private static readonly byte[] Program = PdfBuilder.BuildMinimalTrueTypeFont();

    [Fact]
    public void ParallelDocumentsSaveTheSameSubsetAsOneBuiltAlone()
    {
        var alone = Length1(Build());
        for (var round = 0; round < Rounds; round++)
        {
            var saved = new byte[Threads][];
            Parallel.For(0, Threads, new ParallelOptions { MaxDegreeOfParallelism = Threads }, i => saved[i] = Build());
            for (var i = 0; i < Threads; i++)
                Assert.True(alone == Length1(saved[i]), $"round {round} document {i}: subset /Length1 {Length1(saved[i])}, built alone {alone}");
        }
    }

    // One fragment per printable ASCII character, so nearly every draw adds a glyph to the font.
    private static byte[] Build()
    {
        using var doc = new Document();
        var page = doc.Pages.Add();
        var face = FontRepository.OpenFont(new MemoryStream(Program), FontTypes.TTF);
        var builder = new TextBuilder(page);
        for (var c = '!'; c <= '~'; c++)
        {
            var fragment = new TextFragment(c.ToString()) { Position = new Position(20 + (c % 16) * 30, 800 - (c / 16) * 30) };
            fragment.TextState.Font = face;
            fragment.TextState.FontSize = 12;
            builder.AppendText(fragment);
        }
        using var output = new MemoryStream();
        doc.Save(output);
        return output.ToArray();
    }

    // A font program's dictionary is never inside an object stream, so its /Length1 stands in the raw bytes.
    private static int Length1(byte[] pdf) =>
        int.Parse(Regex.Match(Compat.Latin1.GetString(pdf), @"/Length1 (\d+)").Groups[1].Value);
}
