(() => {
  const body = document.body;
  const toggle = document.querySelector('.nav-toggle');
  const nav = document.querySelector('.main-nav');
  if (toggle && nav) {
    toggle.addEventListener('click', () => {
      const open = toggle.getAttribute('aria-expanded') === 'true';
      toggle.setAttribute('aria-expanded', String(!open));
      nav.classList.toggle('is-open', !open);
    });
  }

  const current = body.dataset.page;
  document.querySelector(`[data-nav="${current}"]`)?.setAttribute('aria-current', 'page');
  let currentReleaseVersion = '2.0.1';
  const form = document.querySelector('[data-feedback-form]');
  const initialAppVersion = form?.elements.appVersion?.value;

  const observer = new IntersectionObserver((entries) => {
    entries.forEach((entry) => {
      if (entry.isIntersecting) {
        entry.target.classList.add('is-visible');
        observer.unobserve(entry.target);
      }
    });
  }, { threshold: 0.08 });
  document.querySelectorAll('.reveal').forEach((el) => observer.observe(el));

  async function loadRelease() {
    const size = document.querySelector('[data-file-size]');
    const hash = document.querySelector('[data-sha256]');
    try {
      const response = await fetch('/release.json', { cache: 'no-store' });
      if (!response.ok) throw new Error('release metadata unavailable');
      const release = await response.json();
      currentReleaseVersion = release.version;
      document.querySelectorAll('[data-release-version]').forEach(el => { el.textContent = release.tag; });
      document.querySelectorAll('[data-release-number]').forEach(el => { el.textContent = release.version; });
      const preview = /preview|pre-?release|alpha|beta|nightly|\brc\b/i.test(release.channel || release.version) || release.prerelease === true;
      const channelLabel = preview ? '预览版' : '正式版';
      document.querySelectorAll('[data-release-channel]').forEach(el => { el.textContent = channelLabel; });
      const date = document.querySelector('[data-published-at]');
      if (date) date.textContent = release.publishedAt.replaceAll('-', '.');
      const notes = document.querySelector('[data-release-notes]');
      if (notes) notes.href = release.releaseUrl;
      if (current === 'download') document.title = `下载 AMD DLSS MU ${release.tag} · ${channelLabel}`;
      if (form && form.elements.appVersion.value === initialAppVersion) form.elements.appVersion.value = release.version;
      if (size) size.textContent = release.sizeDisplay;
      if (hash) hash.textContent = release.sha256;
    } catch {
      if (size) size.textContent = '请以下载文件为准';
      if (hash) hash.textContent = '暂时无法读取校验值';
    }
  }
  loadRelease();

  async function copyText(value) {
    if (navigator.clipboard && window.isSecureContext) return navigator.clipboard.writeText(value);
    const helper = document.createElement('textarea');
    helper.value = value;
    helper.setAttribute('readonly', '');
    helper.style.position = 'fixed';
    helper.style.opacity = '0';
    document.body.appendChild(helper);
    helper.select();
    const copied = document.execCommand('copy');
    helper.remove();
    if (!copied) throw new Error('copy unavailable');
  }

  document.querySelector('[data-copy-hash]')?.addEventListener('click', async (event) => {
    const value = document.querySelector('[data-sha256]')?.textContent?.trim();
    if (!value || value.length !== 64) return;
    const button = event.currentTarget;
    const original = button.textContent;
    try {
      await copyText(value);
      window.muAnalytics?.track('hash_copy');
      button.textContent = '已复制';
    } catch {
      button.textContent = '请手动复制';
    }
    setTimeout(() => { button.textContent = original; }, 1600);
  });

  if (form) {
    const textarea = form.elements.details;
    const counter = form.querySelector('[data-char-count]');
    const status = form.querySelector('[data-form-status]');
    textarea.addEventListener('input', () => { counter.textContent = String(textarea.value.length); });
    form.addEventListener('submit', async (event) => {
      event.preventDefault();
      const button = form.querySelector('button[type="submit"]');
      status.className = 'form-status';
      status.textContent = '';
      button.disabled = true;
      button.firstChild.textContent = '正在发送 ';
      const payload = Object.fromEntries(new FormData(form).entries());
      payload.privacyConfirmed = Boolean(form.elements.privacyConfirmed.checked);
      try {
        const response = await fetch('/api/feedback', {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify(payload)
        });
        const result = await response.json();
        if (!response.ok) throw new Error(result.message || '提交失败，请稍后重试。');
        status.className = 'form-status success';
        status.textContent = `反馈已收到，编号：${result.id}`;
        form.reset();
        form.elements.appVersion.value = currentReleaseVersion;
        counter.textContent = '0';
      } catch (error) {
        status.className = 'form-status error';
        status.textContent = error.message || '提交失败，请稍后重试。';
      } finally {
        button.disabled = false;
        button.firstChild.textContent = '发送反馈 ';
      }
    });
  }
})();
