using EntityTracker.Application.Projects;
using EntityTracker.Domain;
using EntityTracker.Reporting;
using EntityTracker.Wpf.Services;

namespace EntityTracker.Wpf.ViewModels;

public sealed class DashboardViewModelFactory(
    PortfolioQueryService portfolioQueryService,
    ProjectEntityComparisonQueryService comparisonQueryService,
    AggregateProgressReportingService reportingService,
    ProgressChartPresentationBuilder presentationBuilder,
    ProjectRepositoryCardViewModelFactory? repositoryCardFactory = null,
    NotificationCenter? notifications = null)
{
    public PortfolioDashboardViewModel CreatePortfolio() => new(
        portfolioQueryService,
        reportingService,
        presentationBuilder,
        notifications);

    public ProjectDashboardViewModel CreateProject(ProjectId projectId) => new(
        projectId,
        portfolioQueryService,
        comparisonQueryService,
        reportingService,
        presentationBuilder,
        repositoryCardFactory?.Create(projectId),
        notifications);
}
