using Spectre.Console;
using Umbraco.Cli.Infrastructure.Output;

namespace Umbraco.Cli.Tests;

/// <summary>
/// The human writer's object output (#348): it used to print only "Done" for every object result,
/// so a <c>get</c> showed nothing and a <c>create</c> hid the new id.
/// </summary>
public class HumanOutputWriterTests
{
    /// <summary>Runs <paramref name="write"/> against a wide, colourless in-memory console.</summary>
    /// <param name="write">The writes to make.</param>
    /// <returns>Everything written.</returns>
    private static string Render(Action<HumanOutputWriter> write)
    {
        var sw = new StringWriter();
        var console = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.No,
                ColorSystem = ColorSystemSupport.NoColors,
                Out = new AnsiConsoleOutput(sw),
            }
        );
        console.Profile.Width = 200;
        write(new HumanOutputWriter(console));
        return sw.ToString();
    }

    [Fact]
    public void WriteSuccess_Object_ShowsItsKeysAndValues()
    {
        var id = Guid.NewGuid();

        var output = Render(w => w.WriteSuccess(new { Id = id, Name = "About" }));

        Assert.Contains(id.ToString(), output);
        Assert.Contains("name", output);
        Assert.Contains("About", output);
        Assert.DoesNotContain("Done", output);
    }

    [Fact]
    public void WriteSuccess_NestedValue_IsShownAsCompactJson()
    {
        var output = Render(w => w.WriteSuccess(new { Cultures = new[] { "en-US", "da-DK" } }));

        Assert.Contains("[\"en-US\",\"da-DK\"]", output);
    }

    [Fact]
    public void WriteSuccess_ListOfObjects_ShowsATableRowPerItem()
    {
        var output = Render(w =>
            w.WriteSuccess(new[] { new { Name = "Home" }, new { Name = "About" } })
        );

        Assert.Contains("Home", output);
        Assert.Contains("About", output);
    }

    [Fact]
    public void WriteSuccess_Scalar_ShowsTheValue()
    {
        var output = Render(w => w.WriteSuccess(true));

        Assert.Contains("true", output);
    }

    [Fact]
    public void WriteSuccess_EmptyObject_SaysDone()
    {
        var output = Render(w => w.WriteSuccess(new { }));

        Assert.Contains("Done", output);
    }

    [Fact]
    public void WriteSuccess_Null_SaysDone()
    {
        var output = Render(w => w.WriteSuccess<object?>(null));

        Assert.Contains("Done", output);
    }

    [Fact]
    public void WriteSuccess_ValueWithMarkupCharacters_IsWrittenLiterally()
    {
        // A name like "[red]" must not be parsed as Spectre markup (it would throw or vanish).
        var output = Render(w => w.WriteSuccess(new { Name = "[red]x[/]" }));

        Assert.Contains("[red]x[/]", output);
    }
}
