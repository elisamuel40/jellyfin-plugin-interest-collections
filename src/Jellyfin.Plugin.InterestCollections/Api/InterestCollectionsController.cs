using System;
using System.Collections.Generic;
using System.Net.Mime;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.InterestCollections.Configuration;
using Jellyfin.Plugin.InterestCollections.Models;
using Jellyfin.Plugin.InterestCollections.Providers;
using Jellyfin.Plugin.InterestCollections.Services;
using Jellyfin.Plugin.InterestCollections.Storage;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.InterestCollections.Api;

/// <summary>
/// Backs the plugin's configuration and Interest Manager pages.
/// </summary>
/// <remarks>
/// Every endpoint requires an elevated (administrator) session, because they expose library
/// statistics and can trigger work on the server. No endpoint ever returns the configured API key.
/// </remarks>
[ApiController]
[Authorize(Policy = "RequiresElevation")]
[Route("InterestCollections")]
[Produces(MediaTypeNames.Application.Json)]
public sealed class InterestCollectionsController : ControllerBase
{
    private readonly InterestProviderFactory _providerFactory;
    private readonly InterestProcessingService _processor;
    private readonly InterestStatisticsService _statistics;
    private readonly InterestTaxonomy _taxonomy;
    private readonly InterestCache _cache;
    private readonly ILogger<InterestCollectionsController> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="InterestCollectionsController"/> class.
    /// </summary>
    /// <param name="providerFactory">Resolves interest providers.</param>
    /// <param name="processor">The processing pipeline.</param>
    /// <param name="statistics">Builds the Interest Manager rows.</param>
    /// <param name="taxonomy">The interest taxonomy.</param>
    /// <param name="cache">The provider answer cache.</param>
    /// <param name="logger">The logger.</param>
    /// <exception cref="ArgumentNullException">A required dependency is null.</exception>
    public InterestCollectionsController(
        InterestProviderFactory providerFactory,
        InterestProcessingService processor,
        InterestStatisticsService statistics,
        InterestTaxonomy taxonomy,
        InterestCache cache,
        ILogger<InterestCollectionsController> logger)
    {
        ArgumentNullException.ThrowIfNull(providerFactory);
        ArgumentNullException.ThrowIfNull(processor);
        ArgumentNullException.ThrowIfNull(statistics);
        ArgumentNullException.ThrowIfNull(taxonomy);
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(logger);

        _providerFactory = providerFactory;
        _processor = processor;
        _statistics = statistics;
        _taxonomy = taxonomy;
        _cache = cache;
        _logger = logger;
    }

    /// <summary>
    /// Checks that the selected provider answers with the current settings.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the check.</param>
    /// <returns>The outcome of the check.</returns>
    [HttpPost("TestConnection")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<ProviderTestResult>> TestConnection(CancellationToken cancellationToken)
    {
        var provider = _providerFactory.GetCurrent();

        try
        {
            var result = await provider.TestConnectionAsync(cancellationToken).ConfigureAwait(false);
            return Ok(result);
        }
#pragma warning disable CA1031 // The page must show a message rather than a 500 page.
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Connection test for {Provider} failed", provider.Name);
            return Ok(ProviderTestResult.Fail("The test failed unexpectedly. See the server log."));
        }
#pragma warning restore CA1031
    }

    /// <summary>
    /// Runs the whole pipeline without writing anything, and returns what would have changed.
    /// </summary>
    /// <param name="cancellationToken">Token used to cancel the run.</param>
    /// <returns>The dry-run report.</returns>
    [HttpPost("DryRun")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<RunStatistics>> DryRun(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting a dry run requested from the configuration page");
        var statistics = await _processor
            .RunAsync(null, RunOptions.DryRun, cancellationToken)
            .ConfigureAwait(false);

        return Ok(statistics);
    }

    /// <summary>
    /// Lists the interests currently applied across the library.
    /// </summary>
    /// <returns>The Interest Manager rows.</returns>
    [HttpGet("Interests")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<InterestSummary>> GetInterests()
        => Ok(_statistics.GetSummaries());

    /// <summary>
    /// Lists the titles carrying one interest.
    /// </summary>
    /// <param name="key">The interest key.</param>
    /// <returns>The title names.</returns>
    [HttpGet("Interests/{key}/Titles")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<string>> GetInterestTitles([FromRoute] string key)
        => Ok(_statistics.GetTitles(key));

    /// <summary>
    /// Lists the taxonomy categories, for the category checkboxes.
    /// </summary>
    /// <returns>The category names and how many interests each holds.</returns>
    [HttpGet("Categories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<CategorySummary>> GetCategories()
    {
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var definition in _taxonomy.All)
        {
            counts.TryGetValue(definition.Category, out var current);
            counts[definition.Category] = current + 1;
        }

        var results = new List<CategorySummary>(counts.Count);
        foreach (var category in _taxonomy.Categories)
        {
            results.Add(new CategorySummary { Name = category, InterestCount = counts[category] });
        }

        return Ok(results);
    }

    /// <summary>
    /// Reports how much state the plugin currently holds.
    /// </summary>
    /// <returns>The counts.</returns>
    [HttpGet("Status")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public ActionResult<PluginStatus> GetStatus() => Ok(new PluginStatus
    {
        Provider = _providerFactory.GetCurrent().Name,
        ProviderConfigured = _providerFactory.GetCurrent().IsConfigured,
        CachedAnswers = _cache.Count,
        TaxonomySize = _taxonomy.All.Count,
    });
}
