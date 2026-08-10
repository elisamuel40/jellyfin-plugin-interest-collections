const pluginId = '5f9a1c74-3d0e-4c1b-9f2a-7b6d8e0a4c31';

const checkboxes = [
    'ProcessMovies', 'ProcessSeries', 'ProcessEpisodes', 'ProcessOnLibraryEvents',
    'WriteTags', 'LockTagsField', 'ManageCollections', 'AddNewTitlesToCollections',
    'RemoveCollectionsBelowMinimum', 'AllowFranchiseCollections',
    'ExcludeGenreLevelInterests', 'RejectInterestMatchingTitle', 'DryRun'
];

const numbers = [
    'EventDebounceSeconds', 'MinimumTitlesPerCollection', 'MaxConcurrentRequests',
    'RequestDelayMilliseconds', 'RequestTimeoutSeconds', 'MaxRetries',
    'CacheExpirationDays', 'NegativeCacheExpirationDays'
];

const texts = [
    'ApiKey', 'ApiBaseUrl', 'IncludedLibraries', 'CollectionNamePrefix',
    'IgnoredInterests', 'BlockedPatterns', 'InterestAliases'
];

const providerNotes = {
    '0': 'Reads genuine IMDb Interests. No API key needed. See the note at the bottom of this page.',
    '1': 'Uses TMDb keywords, kept only when they map onto the bundled interest taxonomy. Needs a free TMDb API key.',
    '2': 'Derives interests from the genres and tags Jellyfin already holds. Never touches the network.'
};

function apiUrl(path) {
    return ApiClient.getUrl('InterestCollections/' + path);
}

function splitLines(value) {
    return (value || '').split('\n').map(line => line.trim()).filter(line => line.length > 0);
}

function renderCategories(view, selected) {
    const container = view.querySelector('#categoryCheckboxes');

    return ApiClient.getJSON(apiUrl('Categories')).then(categories => {
        const excluded = new Set(selected.map(name => name.toLowerCase()));

        container.innerHTML = categories.map(category => {
            const checked = excluded.has(category.Name.toLowerCase()) ? ' checked' : '';
            return '<label class="checkboxContainer">'
                + '<input is="emby-checkbox" type="checkbox" class="categoryToggle" data-category="'
                + category.Name + '"' + checked + ' />'
                + '<span>' + category.Name + ' (' + category.InterestCount + ')</span>'
                + '</label>';
        }).join('');
    }).catch(() => {
        container.textContent = 'Could not load the categories.';
    });
}

function readExcludedCategories(view) {
    return Array.from(view.querySelectorAll('.categoryToggle'))
        .filter(toggle => toggle.checked)
        .map(toggle => toggle.getAttribute('data-category'))
        .join('\n');
}

function loadStatus(view) {
    return ApiClient.getJSON(apiUrl('Status')).then(status => {
        const suffix = status.ProviderConfigured ? '' : ' — not configured yet';
        view.querySelector('#statusLine').textContent =
            'Provider: ' + status.Provider + suffix
            + ' · ' + status.CachedAnswers + ' cached answers'
            + ' · ' + status.TaxonomySize + ' interests in the bundled taxonomy';
    }).catch(() => {
        view.querySelector('#statusLine').textContent = 'Could not read the plugin status.';
    });
}

function load(view) {
    Dashboard.showLoadingMsg();

    return ApiClient.getPluginConfiguration(pluginId).then(config => {
        checkboxes.forEach(id => { view.querySelector('#' + id).checked = !!config[id]; });
        numbers.forEach(id => { view.querySelector('#' + id).value = config[id]; });
        texts.forEach(id => { view.querySelector('#' + id).value = config[id] || ''; });

        view.querySelector('#Provider').value = String(config.Provider);
        view.querySelector('#providerDescription').textContent = providerNotes[String(config.Provider)] || '';

        return renderCategories(view, splitLines(config.ExcludedCategories));
    }).then(() => loadStatus(view)).finally(() => Dashboard.hideLoadingMsg());
}

function save(view) {
    Dashboard.showLoadingMsg();

    return ApiClient.getPluginConfiguration(pluginId).then(config => {
        checkboxes.forEach(id => { config[id] = view.querySelector('#' + id).checked; });
        numbers.forEach(id => { config[id] = parseInt(view.querySelector('#' + id).value, 10); });
        texts.forEach(id => { config[id] = view.querySelector('#' + id).value; });

        config.Provider = parseInt(view.querySelector('#Provider').value, 10);
        config.ExcludedCategories = readExcludedCategories(view);

        return ApiClient.updatePluginConfiguration(pluginId, config);
    }).then(result => {
        Dashboard.processPluginConfigurationUpdateResult(result);
        return loadStatus(view);
    }).finally(() => Dashboard.hideLoadingMsg());
}

function describeDryRun(report) {
    const lines = [
        'Items: ' + report.ProcessedItems + ' processed of ' + report.TotalItems,
        'Skipped without a provider id: ' + report.SkippedWithoutProviderId,
        'Provider requests: ' + report.ProviderRequests + ' · cache hits: ' + report.CacheHits,
        'Errors: ' + report.Errors + ' (their existing tags were left alone)',
        'Interests found: ' + report.InterestsDiscovered + ' · qualifying: ' + report.InterestsQualifying,
        'Items whose tags would change: ' + report.ItemsTagged,
        ''
    ];

    const collections = report.Collections || {};
    const section = (title, values) => {
        if (values && values.length) {
            lines.push(title + ':');
            values.slice(0, 40).forEach(value => lines.push('  ' + value));
            if (values.length > 40) {
                lines.push('  … and ' + (values.length - 40) + ' more');
            }
            lines.push('');
        }
    };

    section('Collections to create', collections.Created);
    section('Collections to delete', collections.Deleted);
    section('Members to add', collections.MembersAdded);
    section('Members to remove', collections.MembersRemoved);
    section('Below the minimum', collections.BelowMinimum);
    section('Sample tag changes', report.SampleTagChanges);

    return lines.join('\n');
}

export default function (view) {
    view.addEventListener('viewshow', () => load(view));

    view.querySelector('#InterestCollectionsConfigForm').addEventListener('submit', event => {
        event.preventDefault();
        save(view);
        return false;
    });

    view.querySelector('#Provider').addEventListener('change', event => {
        view.querySelector('#providerDescription').textContent = providerNotes[event.target.value] || '';
    });

    view.querySelector('#openInterestManager').addEventListener('click', event => {
        event.preventDefault();
        Dashboard.navigate('configurationpage?name=InterestCollectionsManager');
    });

    view.querySelector('#testConnection').addEventListener('click', () => {
        const output = view.querySelector('#testResult');
        output.textContent = 'Testing…';

        // Save first, so the test uses what is on screen rather than what was stored earlier.
        save(view)
            .then(() => ApiClient.ajax({ type: 'POST', url: apiUrl('TestConnection'), dataType: 'json' }))
            .then(result => { output.textContent = result.Message; })
            .catch(() => { output.textContent = 'The test could not be run. See the server log.'; });
    });

    view.querySelector('#runDryRun').addEventListener('click', () => {
        const output = view.querySelector('#dryRunReport');
        output.textContent = 'Running… this can take a while on a large library.';
        Dashboard.showLoadingMsg();

        ApiClient.ajax({ type: 'POST', url: apiUrl('DryRun'), dataType: 'json' })
            .then(report => { output.textContent = describeDryRun(report); })
            .catch(() => { output.textContent = 'The dry run failed. See the server log.'; })
            .finally(() => Dashboard.hideLoadingMsg());
    });
}
