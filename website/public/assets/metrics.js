(() => {
  if (!['home', 'download', 'guide', 'feedback'].includes(document.body.dataset.page)) return;
  const key = 'mu_analytics_choice_v1';
  const browserOptOut = navigator.globalPrivacyControl === true || navigator.doNotTrack === '1';
  let choice = '';
  let pageSession = '';
  let feedbackStarted = false;
  try { choice = localStorage.getItem(key) || ''; } catch { /* The page works without storage. */ }
  if (browserOptOut) choice = 'denied';
  const campaignQuery = new URLSearchParams(location.search);
  const campaign = /^[a-zA-Z0-9_-]{1,48}$/.test(campaignQuery.get('utm_campaign') || '') ? campaignQuery.get('utm_campaign') : '';
  const knownSources = ['github', 'bilibili', 'douyin', 'xiaohongshu', 'search'];
  const pagePath = { home: '/', download: '/download', guide: '/guide', feedback: '/feedback' }[document.body.dataset.page];
  function source() {
    const tagged = campaignQuery.get('utm_source');
    if (knownSources.includes(tagged)) return tagged;
    if (tagged) return 'other';
    if (!document.referrer) return 'direct';
    try {
      const host = new URL(document.referrer).hostname;
      if (host === location.hostname) return 'direct';
      if (/(^|\.)github\.com$/.test(host)) return 'github';
      if (/(^|\.)bilibili\.com$/.test(host)) return 'bilibili';
      if (/(^|\.)douyin\.com$/.test(host)) return 'douyin';
      if (/(^|\.)xiaohongshu\.com$/.test(host)) return 'xiaohongshu';
      if (/(^|\.)(google\.[a-z.]+|bing\.com|baidu\.com|duckduckgo\.com)$/.test(host)) return 'search';
    } catch { /* Unknown referrers are grouped, never transmitted. */ }
    return 'other';
  }
  const attribution = { source: source(), campaign };
  const cookieTail = `; Path=/; SameSite=Lax${location.protocol === 'https:' ? '; Secure' : ''}`;

  function currentSession() {
    let value = document.cookie.split(';').map(part => part.trim()).find(part => part.startsWith('mu_visit='))?.slice(9);
    if (!/^[a-f0-9-]{36}$/i.test(value || '')) value = crypto.randomUUID();
    document.cookie = `mu_visit=${value}; Max-Age=1800${cookieTail}`;
    return value;
  }

  function send(events) {
    const payload = JSON.stringify({ events });
    const blob = new Blob([payload], { type: 'application/json' });
    if (!navigator.sendBeacon?.('/api/events', blob)) {
      fetch('/api/events', { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: payload, keepalive: true }).catch(() => {});
    }
  }

  function track(name, fields = {}) {
    if (choice !== 'granted' || browserOptOut) return;
    const sid = currentSession();
    const events = [];
    const event = value => ({ id: crypto.randomUUID(), name: value, page: pagePath, ...attribution, ...fields });
    if (pageSession !== sid) { pageSession = sid; events.push(event('page_view')); }
    if (name !== 'page_view') events.push(event(name));
    if (events.length) send(events);
  }
  window.muAnalytics = { track };

  const panel = document.createElement('section');
  panel.className = 'metrics-choice';
  panel.setAttribute('aria-label', '匿名统计设置');
  panel.innerHTML = '<div><strong>帮助我们改进官网</strong><p>允许匿名统计页面访问与下载操作。使用 30 分钟会话标识，不记录原始 IP、联系方式或设备指纹。下载请求总量仍由服务器汇总。</p><small>明细保留 90 天，可随时在页脚修改选择。</small></div><div class="metrics-actions"><button type="button" data-metrics-allow>允许匿名统计</button><button type="button" data-metrics-deny>仅保留必要请求</button></div>';
  panel.hidden = Boolean(choice);
  document.body.appendChild(panel);
  const settings = document.createElement('button');
  settings.type = 'button';
  settings.className = 'metrics-settings';
  settings.textContent = '统计设置';
  document.querySelector('.footer-links')?.appendChild(settings);
  settings.addEventListener('click', () => { panel.hidden = false; panel.querySelector('button:not(:disabled)')?.focus(); });
  function choose(value) {
    choice = value;
    try { localStorage.setItem(key, value); } catch { /* Consent remains in memory for this page. */ }
    panel.hidden = true;
    if (value === 'denied') {
      document.cookie = `mu_visit=; Max-Age=0${cookieTail}`;
      pageSession = '';
    } else track('page_view');
  }
  panel.querySelector('[data-metrics-allow]').addEventListener('click', () => choose('granted'));
  panel.querySelector('[data-metrics-deny]').addEventListener('click', () => choose('denied'));
  if (browserOptOut) {
    panel.querySelector('[data-metrics-allow]').disabled = true;
    panel.querySelector('p').textContent = '已遵循浏览器的拒绝跟踪设置，匿名会话统计保持关闭。服务器仅汇总下载请求等必要操作总量。';
    document.cookie = `mu_visit=; Max-Age=0${cookieTail}`;
  }

  document.querySelectorAll('a[href^="/download/file"]').forEach(link => {
    const entry = link.closest('header') ? 'header' : link.closest('footer') ? 'footer' : 'download_card';
    link.href = `/download/file?entry=${entry}`;
    link.addEventListener('click', () => track('download_click', { entry }));
  });
  document.querySelector('[data-feedback-form]')?.addEventListener('input', () => {
    if (!feedbackStarted && choice === 'granted') { feedbackStarted = true; track('feedback_start'); }
  });
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'visible') track('session_heartbeat');
  });
  setInterval(() => { if (document.visibilityState === 'visible') track('session_heartbeat'); }, 60000);
  track('page_view');
})();
