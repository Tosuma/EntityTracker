using System.IO;
using System.Reflection;
using System.Xml.Linq;

using EntityTracker.Reporting.ProjectReports;

namespace EntityTracker.Wpf.Tests;

public sealed class AppCreditsTests
{
    [Fact]
    public void TheCopyrightMatchesTheLicence()
    {
        string licence = File.ReadAllText(Path.Combine(RepositoryRoot(), "LICENSE"));

        Assert.Contains($"Copyright (c) {AppCredits.Year} {AppCredits.LongName}", licence, StringComparison.Ordinal);
        Assert.Equal("© 2026 Tobias Surland Madsen", AppCredits.CopyrightLine);
        Assert.Equal("Tobias Surland Madsen (Tosuma)", AppCredits.CreatorLine);
        Assert.Equal("by Tobias S. Madsen", AppCredits.ByLine);
    }

    [Fact]
    public void ReportsNameTheSameCreatorAsTheApp() =>
        Assert.Equal(AppCredits.MediumName, ProjectReportHtmlWriter.CreatorName);

    [Fact]
    public void TheAppsFilePropertiesNameTheCreator()
    {
        Assembly app = typeof(AppCredits).Assembly;

        Assert.Equal(AppCredits.LongName, app.GetCustomAttribute<AssemblyCompanyAttribute>()?.Company);
        Assert.Equal(AppCredits.CopyrightLine, app.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright);
        Assert.Equal("EntityTracker", app.GetCustomAttribute<AssemblyProductAttribute>()?.Product);
    }

    [Fact]
    public void TheSidebarAndAboutPageShowTheCreator()
    {
        XDocument window = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "EntityTracker.Wpf", "MainWindow.xaml"));
        XDocument settings = XDocument.Load(Path.Combine(RepositoryRoot(), "src", "EntityTracker.Wpf", "Views", "SettingsView.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        XElement sidebarCredit = Named(window, x, "SidebarCreatorLine");
        Assert.Equal("{x:Static app:AppCredits.ByLine}", (string?)sidebarCredit.Attribute("Text"));
        // The credit sits directly under the app's name and stays dim.
        Assert.Equal("0.6", (string?)sidebarCredit.Attribute("Opacity"));
        Assert.DoesNotContain(window.Descendants(), element =>
            (string?)element.Attribute("Text") == "Implementation portfolio");
        Assert.Equal("{x:Static app:AppCredits.CreatorLine}", (string?)Named(settings, x, "AboutCreatorLine").Attribute("Text"));
        Assert.Equal("{x:Static app:AppCredits.CopyrightLine}", (string?)Named(settings, x, "AboutCopyrightLine").Attribute("Text"));
        Assert.Contains(settings.Descendants(), element =>
            (string?)element.Attribute("Text") == "Open-source under the MIT License.");
    }

    private static XElement Named(XDocument document, XNamespace x, string name) =>
        Assert.Single(document.Descendants(), element => (string?)element.Attribute(x + "Name") == name);

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EntityTracker.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("The repository root was not found.");
    }
}
