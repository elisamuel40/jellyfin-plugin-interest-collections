const pluginId = '5f9a1c74-3d0e-4c1b-9f2a-7b6d8e0a4c31';

let allInterests = [];

function apiUrl(path) {
    return ApiClient.getUrl('InterestCollections/' + path);
}

function escapeHtml(value) {
    return String(value).replace(/[&<>"']/g, character => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    })[character]);
}

function splitLines(value) {
    return (value || '').split('\n').map(line => line.trim()).filter(line => line.length > 0);
}

function renderRows(view, filterText) {
    const needle = (filterText || '').trim().toLowerCase();
    const rows = allInterests.filter(interest =>
        needle.length === 0 || interest.Name.toLowerCase().includes(needle));

    const body = view.querySelector('#interestRows');

    if (rows.length === 0) {
        body.innerHTML = '<tr><td colspan="5">No interests applied yet. Run "Scan new media for interests".</td></tr>';
        return;
    }

    body.innerHTML = rows.map(interest => {
        const disabled = interest.Status === 'Disabled';
        const action = disabled ? 'Enable' : 'Disable';

        return '<tr>'
            + '<td>' + escapeHtml(interest.Name) + '</td>'
            + '<td>' + escapeHtml(interest.Category) + '</td>'
            + '<td style="text-align: right;">' + interest.TitleCount + '</td>'
            + '<td>' + escapeHtml(interest.Status) + '</td>'
            + '<td>'
            + '<button is="emby-button" type="button" class="raised toggleInterest" data-name="'
            + escapeHtml(interest.Name) + '" data-disabled="' + disabled + '"><span>' + action + '</span></button> '
            + '<button is="emby-button" type="button" class="raised showTitles" data-key="'
            + escapeHtml(interest.Key) + '" data-name="' + escapeHtml(interest.Name)
            + '"><span>Titles</span></button>'
            + '</td>'
            + '</tr>';
    }).join('');

    body.querySelectorAll('.toggleInterest').forEach(button => {
        button.addEventListener('click', () => toggleInterest(view, button.getAttribute('data-name'),
            button.getAttribute('data-disabled') === 'true'));
    });

    body.querySelectorAll('.showTitles').forEach(button => {
        button.addEventListener('click', () => showTitles(view, button.getAttribute('data-key'),
            button.getAttribute('data-name')));
    });
}

function toggleInterest(view, name, currentlyDisabled) {
    Dashboard.showLoadingMsg();

    ApiClient.getPluginConfiguration(pluginId).then(config => {
        const disabled = splitLines(config.DisabledInterests);
        const lower = name.toLowerCase();
        const without = disabled.filter(entry => entry.toLowerCase() !== lower);

        if (!currentlyDisabled) {
            without.push(name);
        }

        config.DisabledInterests = without.join('\n');
        return ApiClient.updatePluginConfiguration(pluginId, config);
    }).then(() => load(view)).finally(() => Dashboard.hideLoadingMsg());
}

function showTitles(view, key, name) {
    const panel = view.querySelector('#titlesPanel');
    const list = view.querySelector('#titlesList');

    view.querySelector('#titlesHeading').textContent = 'Titles tagged ' + name;
    panel.style.display = '';
    list.textContent = 'Loading…';

    ApiClient.getJSON(apiUrl('Interests/' + encodeURIComponent(key) + '/Titles'))
        .then(titles => {
            list.innerHTML = titles.length
                ? titles.map(title => escapeHtml(title)).join('<br />')
                : 'No titles found.';
        })
        .catch(() => { list.textContent = 'Could not load the titles.'; });
}

function load(view) {
    return ApiClient.getJSON(apiUrl('Interests')).then(interests => {
        allInterests = interests;

        const withCollections = interests.filter(interest => interest.HasCollection).length;
        view.querySelector('#managerSummary').textContent =
            interests.length + ' interests applied · ' + withCollections + ' with a collection';

        renderRows(view, view.querySelector('#interestSearch').value);
    }).catch(() => {
        view.querySelector('#interestRows').innerHTML =
            '<tr><td colspan="5">Could not load the interests. See the server log.</td></tr>';
    });
}

export default function (view) {
    view.addEventListener('viewshow', () => load(view));

    view.querySelector('#interestSearch').addEventListener('input', event => {
        renderRows(view, event.target.value);
    });

    view.querySelector('#backToSettings').addEventListener('click', event => {
        event.preventDefault();
        Dashboard.navigate('configurationpage?name=Interest%20Collections');
    });
}
