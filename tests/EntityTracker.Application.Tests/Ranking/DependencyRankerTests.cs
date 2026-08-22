using EntityTracker.Application.Ranking;
using EntityTracker.Domain;

namespace EntityTracker.Application.Tests.Ranking;

public sealed class DependencyRankerTests
{
    private readonly DependencyRanker _ranker = new();

    [Fact]
    public void Rank_EmptyGraph_ReturnsSuccessfulEmptyResult()
    {
        DependencyRankingResult result = _ranker.Rank([], []);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Rankings);
        Assert.Empty(result.Diagnostics);
    }

    [Fact]
    public void Rank_Chain_OrdersDependenciesFirstAndReturnsImpactMetadata()
    {
        TrackedEntity foundation = Entity(1, "Foundation");
        TrackedEntity service = Entity(2, "Service");
        TrackedEntity userInterface = Entity(3, "UserInterface");
        DependencyEdge serviceDependsOnFoundation = new(service.Id, foundation.Id);
        DependencyEdge userInterfaceDependsOnService = new(userInterface.Id, service.Id);

        DependencyRankingResult result = _ranker.Rank(
            [userInterface, foundation, service],
            [userInterfaceDependsOnService, serviceDependsOnFoundation]);

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [foundation.Id, service.Id, userInterface.Id],
            result.Rankings.Select(static ranking => ranking.EntityId));
        Assert.Equal([1, 2, 3], result.Rankings.Select(static ranking => ranking.Rank));
        Assert.Equal([2, 1, 0], result.Rankings.Select(static ranking => ranking.ImpactScore));

        EntityRanking serviceRanking = RankingFor(result, service.Id);
        Assert.Equal([foundation.Id], serviceRanking.DirectDependencies);
        Assert.Equal([userInterface.Id], serviceRanking.DirectDependents);
    }

    [Fact]
    public void Rank_Diamond_CountsUniqueDownstreamEntitiesAndBreaksTiesByName()
    {
        TrackedEntity root = Entity(1, "Root");
        TrackedEntity charlie = Entity(2, "Charlie");
        TrackedEntity beta = Entity(3, "Beta");
        TrackedEntity leaf = Entity(4, "Leaf");
        DependencyEdge[] edges =
        [
            new(beta.Id, root.Id),
            new(charlie.Id, root.Id),
            new(leaf.Id, beta.Id),
            new(leaf.Id, charlie.Id)
        ];

        DependencyRankingResult result = _ranker.Rank(
            [leaf, charlie, root, beta],
            edges.Reverse());

        Assert.True(result.IsSuccess);
        Assert.Equal(
            [root.Id, beta.Id, charlie.Id, leaf.Id],
            result.Rankings.Select(static ranking => ranking.EntityId));
        Assert.Equal(3, RankingFor(result, root.Id).ImpactScore);
        Assert.Equal(1, RankingFor(result, beta.Id).ImpactScore);
        Assert.Equal(1, RankingFor(result, charlie.Id).ImpactScore);
        Assert.Equal(0, RankingFor(result, leaf.Id).ImpactScore);
        Assert.Equal(
            [beta.Id, charlie.Id],
            RankingFor(result, root.Id).DirectDependents);
        Assert.Equal(
            [beta.Id, charlie.Id],
            RankingFor(result, leaf.Id).DirectDependencies);
    }

    [Fact]
    public void Rank_DisconnectedGraph_PrioritizesHigherImpactEligibleEntity()
    {
        TrackedEntity highImpactRoot = Entity(1, "ZuluRoot");
        TrackedEntity middle = Entity(2, "Middle");
        TrackedEntity leaf = Entity(3, "Leaf");
        TrackedEntity lowImpactRoot = Entity(4, "AlphaRoot");
        TrackedEntity lowImpactLeaf = Entity(5, "AlphaLeaf");
        TrackedEntity isolated = Entity(6, "AardvarkIsolated");
        DependencyEdge[] edges =
        [
            new(middle.Id, highImpactRoot.Id),
            new(leaf.Id, middle.Id),
            new(lowImpactLeaf.Id, lowImpactRoot.Id)
        ];

        DependencyRankingResult result = _ranker.Rank(
            [isolated, lowImpactLeaf, highImpactRoot, middle, lowImpactRoot, leaf],
            edges);

        Assert.True(result.IsSuccess);
        Assert.Equal(highImpactRoot.Id, result.Rankings[0].EntityId);
        AssertDependencyOrder(result, edges);
    }

    [Fact]
    public void Rank_SameGraphInDifferentEnumerationOrders_ReturnsSameOrdering()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        TrackedEntity charlie = Entity(3, "Charlie");
        TrackedEntity delta = Entity(4, "Delta");
        DependencyEdge[] edges =
        [
            new(charlie.Id, alpha.Id),
            new(delta.Id, beta.Id)
        ];

        DependencyRankingResult first = _ranker.Rank(
            [alpha, beta, charlie, delta],
            edges);
        DependencyRankingResult second = _ranker.Rank(
            [delta, charlie, beta, alpha],
            edges.Reverse());

        Assert.Equal(
            first.Rankings.Select(static ranking => ranking.EntityId),
            second.Rankings.Select(static ranking => ranking.EntityId));
        Assert.Equal(
            first.Rankings.Select(static ranking => ranking.ImpactScore),
            second.Rankings.Select(static ranking => ranking.ImpactScore));
    }

    [Fact]
    public void Rank_EqualNamesAndImpact_UsesEntityIdAsFinalTieBreaker()
    {
        TrackedEntity higherId = Entity(2, "SameName");
        TrackedEntity lowerId = Entity(1, "samename");

        DependencyRankingResult result = _ranker.Rank([higherId, lowerId], []);

        Assert.Equal(
            [lowerId.Id, higherId.Id],
            result.Rankings.Select(static ranking => ranking.EntityId));
    }

    [Fact]
    public void Rank_Cycle_ReturnsClosedDeterministicPathAndNoOrdering()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        TrackedEntity charlie = Entity(3, "Charlie");
        TrackedEntity acyclic = Entity(4, "Unrelated");
        DependencyEdge[] edges =
        [
            new(alpha.Id, beta.Id),
            new(beta.Id, charlie.Id),
            new(charlie.Id, alpha.Id)
        ];

        DependencyRankingResult result = _ranker.Rank(
            [acyclic, charlie, beta, alpha],
            edges.Reverse());

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Rankings);
        DependencyRankingDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DependencyRankingDiagnosticCode.CycleDetected, diagnostic.Code);
        Assert.Equal(
            [alpha.Id, beta.Id, charlie.Id, alpha.Id],
            diagnostic.RelatedEntityIds);
        Assert.Contains("Alpha -> Beta -> Charlie -> Alpha", diagnostic.Message);
    }

    [Fact]
    public void Rank_UnknownEdgeEndpoints_ReturnsDiagnosticsAndNoOrdering()
    {
        TrackedEntity known = Entity(1, "Known");
        EntityId unknownDependentId = Id(2);
        EntityId unknownDependencyId = Id(3);

        DependencyRankingResult result = _ranker.Rank(
            [known],
            [
                new DependencyEdge(unknownDependentId, known.Id),
                new DependencyEdge(known.Id, unknownDependencyId)
            ]);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Rankings);
        Assert.Equal(2, result.Diagnostics.Count);
        Assert.All(
            result.Diagnostics,
            static diagnostic => Assert.Equal(
                DependencyRankingDiagnosticCode.UnknownEntity,
                diagnostic.Code));
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.RelatedEntityIds.SequenceEqual([unknownDependentId]));
        Assert.Contains(
            result.Diagnostics,
            diagnostic => diagnostic.RelatedEntityIds.SequenceEqual([unknownDependencyId]));
    }

    [Fact]
    public void Rank_UnresolvedEntityIsExcludedWhileResolvedGraphRemainsRanked()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity blocked = Entity(2, "Blocked");
        TrackedEntity charlie = Entity(3, "Charlie");

        DependencyRankingResult result = _ranker.Rank(
            [blocked, charlie, alpha],
            [new DependencyEdge(charlie.Id, alpha.Id)],
            [new UnresolvedDependency(blocked.Id, "MissingX")]);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Diagnostics);
        Assert.Equal(
            [alpha.Id, charlie.Id],
            result.Rankings.Select(static ranking => ranking.EntityId));
        UnrankedEntity unranked = Assert.Single(result.UnrankedEntities);
        Assert.Equal(blocked.Id, unranked.EntityId);
        Assert.Equal(DependencyResolutionState.Unresolved, unranked.State);
        Assert.Equal(["MissingX"], unranked.MissingDependencyNames);
    }

    [Fact]
    public void Rank_KnownAndUnknownDependencies_DoNotAssignDependentRank()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");

        DependencyRankingResult result = _ranker.Rank(
            [beta, alpha],
            [new DependencyEdge(beta.Id, alpha.Id)],
            [new UnresolvedDependency(beta.Id, "MissingX")]);

        Assert.Equal([alpha.Id], result.Rankings.Select(static ranking => ranking.EntityId));
        Assert.Equal(beta.Id, Assert.Single(result.UnrankedEntities).EntityId);
    }

    [Fact]
    public void Rank_PropagatesUnresolvedNamesToTransitiveDependents()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");
        TrackedEntity charlie = Entity(3, "Charlie");

        DependencyRankingResult result = _ranker.Rank(
            [charlie, beta, alpha],
            [
                new DependencyEdge(beta.Id, alpha.Id),
                new DependencyEdge(charlie.Id, beta.Id)
            ],
            [new UnresolvedDependency(alpha.Id, "MissingX")]);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Rankings);
        Assert.Equal(
            [alpha.Id, beta.Id, charlie.Id],
            result.UnrankedEntities.Select(static entity => entity.EntityId));
        Assert.Equal(
            [
                DependencyResolutionState.Unresolved,
                DependencyResolutionState.Blocked,
                DependencyResolutionState.Blocked
            ],
            result.UnrankedEntities.Select(static entity => entity.State));
        Assert.All(result.UnrankedEntities, static entity =>
            Assert.Equal(["MissingX"], entity.MissingDependencyNames));
    }

    [Fact]
    public void Rank_PropagatesMultipleMissingNamesDeterministically()
    {
        TrackedEntity alpha = Entity(1, "Alpha");
        TrackedEntity beta = Entity(2, "Beta");

        DependencyRankingResult result = _ranker.Rank(
            [beta, alpha],
            [new DependencyEdge(beta.Id, alpha.Id)],
            [
                new UnresolvedDependency(alpha.Id, "ZuluMissing"),
                new UnresolvedDependency(alpha.Id, "alphaMissing")
            ]);

        Assert.Equal(
            ["alphaMissing", "ZuluMissing"],
            result.UnrankedEntities[0].MissingDependencyNames);
        Assert.Equal(
            ["alphaMissing", "ZuluMissing"],
            result.UnrankedEntities[1].MissingDependencyNames);
    }

    [Fact]
    public void Rank_UnresolvedReferenceWithUnknownDependent_RemainsGraphError()
    {
        EntityId missingDependentId = Id(2);

        DependencyRankingResult result = _ranker.Rank(
            [Entity(1, "Known")],
            [],
            [new UnresolvedDependency(missingDependentId, "MissingX")]);

        Assert.False(result.IsSuccess);
        Assert.Empty(result.Rankings);
        Assert.Empty(result.UnrankedEntities);
        DependencyRankingDiagnostic diagnostic = Assert.Single(result.Diagnostics);
        Assert.Equal(DependencyRankingDiagnosticCode.UnknownEntity, diagnostic.Code);
        Assert.Equal([missingDependentId], diagnostic.RelatedEntityIds);
    }

    [Fact]
    public void Rank_RecomputesCompleteOrderingAfterGraphChanges()
    {
        TrackedEntity root = Entity(1, "ZuluRoot");
        TrackedEntity dependent = Entity(2, "AlphaDependent");

        DependencyRankingResult beforeChange = _ranker.Rank([root, dependent], []);
        EntityId[] originalOrdering = beforeChange.Rankings
            .Select(static ranking => ranking.EntityId)
            .ToArray();

        DependencyRankingResult afterChange = _ranker.Rank(
            [root, dependent],
            [new DependencyEdge(dependent.Id, root.Id)]);

        Assert.Equal([dependent.Id, root.Id], originalOrdering);
        Assert.Equal(
            [root.Id, dependent.Id],
            afterChange.Rankings.Select(static ranking => ranking.EntityId));
        Assert.Equal(
            originalOrdering,
            beforeChange.Rankings.Select(static ranking => ranking.EntityId));
    }

    [Fact]
    public void Rank_DuplicateEntityIds_ThrowsArgumentException()
    {
        TrackedEntity first = Entity(1, "First");
        TrackedEntity duplicate = new(first.Id, "Duplicate");

        Assert.Throws<ArgumentException>(() => _ranker.Rank([first, duplicate], []));
    }

    [Fact]
    public void Rank_DuplicateEdges_ThrowsArgumentException()
    {
        TrackedEntity dependent = Entity(1, "Dependent");
        TrackedEntity dependency = Entity(2, "Dependency");
        DependencyEdge first = new(dependent.Id, dependency.Id);
        DependencyEdge duplicate = new(dependent.Id, dependency.Id);

        Assert.Throws<ArgumentException>(() =>
            _ranker.Rank([dependent, dependency], [first, duplicate]));
    }

    [Fact]
    public void Rank_NullCollectionElements_ThrowArgumentException()
    {
        TrackedEntity entity = Entity(1, "Entity");
        DependencyEdge edge = new(Id(2), entity.Id);

        Assert.Throws<ArgumentException>(() =>
            _ranker.Rank(new TrackedEntity[] { entity, null! }, []));
        Assert.Throws<ArgumentException>(() =>
            _ranker.Rank([entity], new DependencyEdge[] { edge, null! }));
    }

    [Fact]
    public void Rank_NullCollections_ThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            _ranker.Rank(null!, Array.Empty<DependencyEdge>()));
        Assert.Throws<ArgumentNullException>(() =>
            _ranker.Rank(Array.Empty<TrackedEntity>(), null!));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(19)]
    [InlineData(41)]
    [InlineData(83)]
    public void Rank_GeneratedDag_AlwaysPlacesDependenciesBeforeDependents(int seed)
    {
        Random random = new(seed);
        TrackedEntity[] entities = Enumerable.Range(1, 40)
            .Select(index => Entity(index, $"Entity{index:D2}"))
            .ToArray();
        List<DependencyEdge> edges = [];

        for (int dependentIndex = 1; dependentIndex < entities.Length; dependentIndex++)
        {
            for (int dependencyIndex = 0; dependencyIndex < dependentIndex; dependencyIndex++)
            {
                if (random.NextDouble() < 0.08)
                {
                    edges.Add(new DependencyEdge(
                        entities[dependentIndex].Id,
                        entities[dependencyIndex].Id));
                }
            }
        }

        DependencyRankingResult result = _ranker.Rank(
            Shuffle(entities, random),
            Shuffle(edges, random));

        Assert.True(result.IsSuccess);
        Assert.Equal(entities.Length, result.Rankings.Count);
        AssertDependencyOrder(result, edges);
    }

    private static void AssertDependencyOrder(
        DependencyRankingResult result,
        IEnumerable<DependencyEdge> edges)
    {
        Dictionary<EntityId, int> ranks = result.Rankings.ToDictionary(
            static ranking => ranking.EntityId,
            static ranking => ranking.Rank);

        foreach (DependencyEdge edge in edges)
        {
            Assert.True(
                ranks[edge.DependencyEntityId] < ranks[edge.DependentEntityId],
                $"Expected dependency {edge.DependencyEntityId.Value} to precede " +
                $"dependent {edge.DependentEntityId.Value}.");
        }
    }

    private static EntityRanking RankingFor(
        DependencyRankingResult result,
        EntityId entityId)
    {
        return Assert.Single(result.Rankings, ranking => ranking.EntityId == entityId);
    }

    private static T[] Shuffle<T>(IEnumerable<T> items, Random random)
    {
        return items.OrderBy(_ => random.Next()).ToArray();
    }

    private static TrackedEntity Entity(int id, string name)
    {
        return new TrackedEntity(Id(id), name);
    }

    private static EntityId Id(int value)
    {
        byte[] bytes = new byte[16];
        BitConverter.GetBytes(value).CopyTo(bytes, 0);
        return new EntityId(new Guid(bytes));
    }
}
