(function () {
    const grid = document.getElementById('downloadGrid');

    function esc(value) {
        return String(value ?? '').replace(/[&<>"']/g, char => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
        }[char]));
    }

    function renderCard(platform) {
        const meta = [platform.version, platform.size].filter(Boolean).join(' · ');
        const canDownload = platform.available && platform.url;

        return `
      <article class="download-card${canDownload ? '' : ' download-card-soon'}">
        <img class="download-card-art" src="/recnet/${esc(platform.icon)}" alt="" width="140" height="140">
        <h3>${esc(platform.label)}</h3>
        ${meta ? `<div class="download-card-meta">${esc(meta)}</div>` : ''}
        ${canDownload
            ? `<a class="primary-button download-card-button" href="${esc(platform.url)}">Download</a>`
            : `<span class="download-card-button download-card-disabled">Coming soon</span>`}
      </article>`;
    }

    fetch('/recnet/downloads.json')
        .then(response => response.json())
        .then(platforms => {
            grid.innerHTML = platforms.map(renderCard).join('');
        })
        .catch(() => {
            grid.innerHTML = '<div class="empty">Could not load download links right now. Try again in a moment.</div>';
        });
})();
