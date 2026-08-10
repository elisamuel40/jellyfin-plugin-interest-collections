using System;
using System.Collections.Generic;
using System.Globalization;
using Jellyfin.Plugin.InterestCollections.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.InterestCollections;

/// <summary>
/// Tags movies and series with IMDb-style semantic interests and maintains collections built
/// from those interests.
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// The provider id key stamped onto every collection this plugin owns. A collection without
    /// this key was created by someone else and is never modified or deleted.
    /// </summary>
    public const string OwnershipProviderKey = "InterestCollections";

    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Instance of the <see cref="IApplicationPaths"/> interface.</param>
    /// <param name="xmlSerializer">Instance of the <see cref="IXmlSerializer"/> interface.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Interest Collections";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("5f9a1c74-3d0e-4c1b-9f2a-7b6d8e0a4c31");

    /// <inheritdoc />
    public override string Description =>
        "Classifies movies and shows with granular semantic interests, writes them as tags, " +
        "and optionally maintains a collection per interest.";

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return
        [
            new PluginPageInfo
            {
                Name = Name,
                EmbeddedResourcePath = ResourcePath("configPage.html"),
                EnableInMainMenu = true,
            },
            new PluginPageInfo
            {
                Name = "interestcollections.js",
                EmbeddedResourcePath = ResourcePath("configPage.js"),
            },
            new PluginPageInfo
            {
                Name = "InterestCollectionsManager",
                EmbeddedResourcePath = ResourcePath("interestManager.html"),
            },
            new PluginPageInfo
            {
                Name = "interestcollectionsmanager.js",
                EmbeddedResourcePath = ResourcePath("interestManager.js"),
            },
        ];
    }

    private static string ResourcePath(string fileName)
        => string.Format(
            CultureInfo.InvariantCulture,
            "{0}.Configuration.{1}",
            typeof(Plugin).Namespace,
            fileName);
}
