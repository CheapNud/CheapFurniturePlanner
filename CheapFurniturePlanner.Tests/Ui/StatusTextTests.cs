using CheapFurniturePlanner.Ui;
using Xunit;

namespace CheapFurniturePlanner.Tests.Ui;

// UX-2 Task 2: pins the humanizer's exact output for the enum-string shapes seen in this
// codebase - PascalCase and underscore-shouty - plus the pass-through edge cases.
public class StatusTextTests
{
    [Theory]
    [InlineData("InProgress", "In progress")]
    [InlineData("BackflushUndo", "Backflush undo")]
    [InlineData("RMA_Created", "RMA created")]
    [InlineData("Draft", "Draft")]
    [InlineData("Already spaced", "Already spaced")]
    public void Humanize_ProducesExpectedLabel(string input, string expected) =>
        Assert.Equal(expected, StatusText.Humanize(input));
}
