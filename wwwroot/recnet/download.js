(function () {
    const tabs = document.getElementById('platformTabs');
    const stage = document.getElementById('downloadStage');
    let platforms = [];
    let activeId = null;

    function esc(value) {
        return String(value ?? '').replace(/[&<>"']/g, char => ({
            '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;',
        }[char]));
    }

    function renderTabs() {
        tabs.innerHTML = platforms.map(p => `
      <button class="platform-tab${p.id === activeId ? ' platform-tab-active' : ''}" data-id="${esc(p.id)}" type="button">
        <img src="/recnet/${esc(p.icon)}" alt="" width="36" height="36">
        <span>${esc(p.tabLabel)}</span>
      </button>`).join('');

        tabs.querySelectorAll('.platform-tab').forEach(button => {
            button.addEventListener('click', () => {
                activeId = button.dataset.id;
                renderTabs();
                renderStage();
            });
        });
    }

    function renderStage() {
        const platform = platforms.find(p => p.id === activeId);
        if (!platform) {
            stage.innerHTML = '<div class="empty">Choose a platform above.</div>';
            return;
        }

        const checkboxId = `agree-${platform.id}`;
        const buttonLabel = platform.tabLabel === 'PC' ? 'Play On PC' : `Play On ${esc(platform.tabLabel)}`;

        stage.innerHTML = `
      <section class="download-hero">
        <div class="download-hero-art">
          <img src="/recnet/${esc(platform.icon)}" alt="" width="220" height="220">
        </div>
        <div class="download-hero-content">
          <h1>Mocha for<div>${esc(platform.headline)}</div></h1>
          <p class="download-blurb">${esc(platform.blurb)}</p>
          <p class="download-requirement">${esc(platform.requirement)}</p>
          ${platform.available && platform.url ? `
            <label class="download-terms">
              <input type="checkbox" id="${checkboxId}">
              I agree to the <a href="/privacy">Privacy Policy</a> and <a href="/tos">Terms of Service</a>
            </label>
            <a class="primary-button download-cta" id="downloadCta" href="${esc(platform.url)}" aria-disabled="true">${esc(buttonLabel)}</a>
            ${platform.version || platform.size ? `<p class="download-meta">${[platform.version, platform.size].filter(Boolean).map(esc).join(' · ')}</p>` : ''}
          ` : `
            <span class="download-cta download-cta-soon">Coming soon</span>
          `}
        </div>
      </section>`;

        const checkbox = document.getElementById(checkboxId);
        const cta = document.getElementById('downloadCta');
        if (checkbox && cta) {
            cta.classList.add('download-cta-locked');
            cta.addEventListener('click', event => {
                if (!checkbox.checked) event.preventDefault();
            });
            checkbox.addEventListener('change', () => {
                cta.classList.toggle('download-cta-locked', !checkbox.checked);
                cta.setAttribute('aria-disabled', String(!checkbox.checked));
            });
        }
    }

    fetch('/recnet/downloads.json')
        .then(response => response.json())
        .then(data => {
            platforms = data;
            activeId = (platforms.find(p => p.available) || platforms[0])?.id ?? null;
            renderTabs();
            renderStage();
        })
        .catch(() => {
            stage.innerHTML = '<div class="empty">Could not load download links right now. Try again in a moment.</div>';
        });
})();
