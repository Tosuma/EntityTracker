using System.IO;
using System.Xml.Linq;

namespace EntityTracker.Wpf.Tests;

public sealed class PresentationConfigurationTests
{
    [Fact]
    public void MainWindow_DeclaresOnlyTheSupportedFilterableColumns()
    {
        string repositoryRoot = FindRepositoryRoot(AppContext.BaseDirectory);
        XDocument document = XDocument.Load(Path.Combine(
            repositoryRoot,
            "src",
            "EntityTracker.Wpf",
            "MainWindow.xaml"));

        string[] configuredFilters = document
            .Descendants()
            .Where(element => element.Name.LocalName == "FilterableColumnHeader")
            .Select(element => (string?)element.Attribute("Filter"))
            .OfType<string>()
            .ToArray();

        Assert.Equal(
        [
            "{Binding DataContext.ActiveTable.ResponsibleDeveloperFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ActiveTable.GroupFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ActiveTable.StatusFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ActiveTable.WorkStatusFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ArchivedTable.ResponsibleDeveloperFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ArchivedTable.GroupFilter, RelativeSource={RelativeSource AncestorType=Window}}",
            "{Binding DataContext.ArchivedTable.StatusFilter, RelativeSource={RelativeSource AncestorType=Window}}"
        ],
            configuredFilters);
    }

    private static string FindRepositoryRoot(string startDirectory)
    {
        DirectoryInfo? current = new(startDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "EntityTracker.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate EntityTracker.slnx above '{startDirectory}'.");
    }
}
