using System;
using System.Collections.Generic;

namespace Jellyfin.Plugin.InterestCollections.Storage;

/// <summary>
/// Helpers shared by the plugin's stores.
/// </summary>
public static class StoreKeys
{
    /// <summary>
    /// Creates a dictionary with the comparison semantics the stores expect: keys are compared
    /// ordinally and case-insensitively, matching how provider ids and interest keys behave.
    /// </summary>
    /// <typeparam name="TValue">The value type.</typeparam>
    /// <returns>The dictionary.</returns>
    public static Dictionary<string, TValue> NewDictionary<TValue>()
        => new(StringComparer.OrdinalIgnoreCase);
}
