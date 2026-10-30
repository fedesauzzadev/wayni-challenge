using Bcra.Query.Domain.Ports;
using Bcra.Query.Infrastructure.Persistence;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace Bcra.Query.IntegrationTests;

public sealed class CachedActiveImportResolverTests
{
    [Fact]
    public async Task TwoResolves_WithinTtl_ProduceSingleInnerQuery()
    {
        var importId = Guid.NewGuid();
        var inner = new CountingResolver(importId);
        var sut = CreateResolver(inner, ttlSeconds: 5);

        var first = await sut.ResolveActiveImportIdAsync(CancellationToken.None);
        var second = await sut.ResolveActiveImportIdAsync(CancellationToken.None);

        first.Should().Be(importId);
        second.Should().Be(importId);
        inner.Calls.Should().Be(1, "la segunda resolución dentro del TTL sale de la caché");
    }

    [Fact]
    public async Task TwoResolves_WithoutActiveImport_WithinTtl_ProduceSingleInnerQuery()
    {
        var inner = new CountingResolver(null);
        var sut = CreateResolver(inner, ttlSeconds: 5);

        await sut.ResolveActiveImportIdAsync(CancellationToken.None);
        var second = await sut.ResolveActiveImportIdAsync(CancellationToken.None);

        second.Should().BeNull();
        inner.Calls.Should().Be(1, "el 'sin import activo' también se cachea");
    }

    [Fact]
    public async Task TtlZero_DisablesCache_AndQueriesOncePerResolve()
    {
        var inner = new CountingResolver(Guid.NewGuid());
        var sut = CreateResolver(inner, ttlSeconds: 0);

        await sut.ResolveActiveImportIdAsync(CancellationToken.None);
        await sut.ResolveActiveImportIdAsync(CancellationToken.None);

        inner.Calls.Should().Be(2, "con TTL 0 cada request consulta la BD (comportamiento actual)");
    }

    [Fact]
    public async Task ConcurrentResolves_WhileInnerInFlight_ProduceSingleInnerQueryAndSameImportId()
    {
        var importId = Guid.NewGuid();
        var inner = new GatedResolver(importId);
        var sut = CreateResolver(inner, ttlSeconds: 5);
        using var _ = sut;

        var resolves = Enumerable.Range(0, 8)
            .Select(_ => sut.ResolveActiveImportIdAsync(CancellationToken.None))
            .ToArray();

        await inner.Started;
        inner.Complete();
        var results = await Task.WhenAll(resolves);

        results.Should().OnlyContain(id => id == importId);
        inner.Calls.Should().Be(1, "resoluciones concurrentes durante un miss compartido deben deduplicar el lookup a active_import (anti-stampede)");
    }

    private static CachedActiveImportResolver CreateResolver(IActiveImportResolver inner, int ttlSeconds) =>
        new(inner, new MemoryCache(new MemoryCacheOptions()), Options.Create(new ActiveImportCacheOptions { ActiveImportCacheTtlSeconds = ttlSeconds }));

    private sealed class CountingResolver(Guid? result) : IActiveImportResolver
    {
        public int Calls { get; private set; }

        public Task<Guid?> ResolveActiveImportIdAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(result);
        }
    }

    private sealed class GatedResolver(Guid? result) : IActiveImportResolver
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public Task Started => started.Task;

        public void Complete() => completion.TrySetResult();

        public async Task<Guid?> ResolveActiveImportIdAsync(CancellationToken cancellationToken)
        {
            Calls++;
            started.TrySetResult();
            await completion.Task;
            return result;
        }
    }
}
