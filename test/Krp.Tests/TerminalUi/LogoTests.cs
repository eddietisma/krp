using Krp.Tool.TerminalUi;
using Spectre.Console;

namespace Krp.Tests.TerminalUi;

[TestClass]
public sealed class LogoTests
{
    [TestMethod]
    public void Render_ShouldExcludeFontDelimiters_WhenUsingEmbeddedLogoFont()
    {
        using var stream = typeof(KrpTerminalUi).Assembly.GetManifestResourceStream("Krp.Tool.TerminalUi.Fonts.3D.flf");
        var font = FigletFont.Load(stream!);
        using var writer = new StringWriter();
        var console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = AnsiSupport.No,
            ColorSystem = ColorSystemSupport.NoColors,
            Out = new AnsiConsoleOutput(writer),
        });
        console.Profile.Width = 120;

        console.Write(new FigletText(font, "KRP"));

        var logo = writer.ToString();
        Assert.IsFalse(logo.Contains('@'), logo);
        Assert.IsFalse(logo.Contains('$'), logo);
        Assert.IsTrue(logo.Contains('\\'), logo);
    }
}
