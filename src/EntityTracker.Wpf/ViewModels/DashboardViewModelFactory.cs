using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Reporting;

namespace EntityTracker.Wpf.ViewModels;

public sealed class DashboardViewModelFactory(
    PortfolioQueryService portfolioQueryService,
    ProjectEntityComparisonQueryService comparisonQueryService,
    AggregateProgressReportingService reportingService,
    ProgressChartPresentationBuilder presentationBuilder)
{
    public PortfolioDashboardViewModel CreatePortfolio() => new(
        portfolioQueryService,
        reportingService,
        presentationBuilder);

    public ProjectDashboardViewModel CreateProject(ProjectId projectId) => new(
        projectId,
        portfolioQueryService,
        comparisonQueryService,
        reportingService,
        presentationBuilder);
}
