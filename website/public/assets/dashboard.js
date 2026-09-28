(() => {
  const $ = selector => document.querySelector(selector);
  const api = (...args) => window.muAdmin.api(...args);
  const fmt = value => value === null || value === undefined ? '—' : Number(value).toLocaleString('zh-CN');
  const percent = value => value === null || value === undefined ? '—' : `${(value * 100).toFixed(1)}%`;
  const time = value => new Date(value).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false });
  const statuses = { new: '待处理', in_progress: '处理中', resolved: '已解决', ignored: '暂不处理' };
  const categories = { compatibility: '兼容性', bug: '错误 / 崩溃', performance: '性能表现', suggestion: '功能建议', other: '其他' };
  const sources = { direct: '直接访问 / 站内', github: 'GitHub', bilibili: '哔哩哔哩', douyin: '抖音', xiaohongshu: '小红书', search: '搜索引擎', other: '其他来源' };
  let authenticated = false;
  let currentTab = ['overview', 'release', 'feedback', 'survey', 'tracking'].includes(location.hash.slice(1)) ? location.hash.slice(1) : 'overview';
  let requestId = 0;
  let overviewPending = false;
  let feedbackPage = 1;
  let feedbackPages = 1;
  let selectedFeedback = null;
  let lastOverview = null;

  function element(tag, content = '', className = '') {
    const result = document.createElement(tag);
    if (content !== '') result.textContent = content;
    if (className) result.className = className;
    return result;
  }
  function empty(message, note = '') {
    const result = element('div', message, 'empty-state');
    if (note) result.appendChild(element('small', note));
    return result;
  }
  function table(headers, rows) {
    const result = element('table');
    const head = element('thead');
    const heading = element('tr');
    headers.forEach(label => heading.appendChild(element('th', label)));
    head.appendChild(heading);
    const body = element('tbody');
    for (const cells of rows) {
      const row = element('tr');
      for (const value of cells) {
        const cell = element('td');
        if (value instanceof Node) cell.appendChild(value);
        else cell.textContent = String(value ?? '—');
        row.appendChild(cell);
      }
      body.appendChild(row);
    }
    result.append(head, body);
    return result;
  }
  function badge(status) { return element('span', statuses[status] || status, `status-badge status-${status}`); }
  function progress(value, max) {
    const bar = element('progress');
    bar.max = Math.max(1, max);
    bar.value = value;
    bar.setAttribute('aria-label', `${fmt(value)} / ${fmt(max)}`);
    return bar;
  }
  function distribution(target, items, label, value, note) {
    target.replaceChildren();
    if (!items.length) return target.appendChild(empty('等待真实数据', note));
    const max = Math.max(...items.map(value), 1);
    for (const item of items) {
      const row = element('div', '', 'distribution-row');
      const heading = element('div', '', 'distribution-label');
      heading.append(element('span', label(item)), element('b', fmt(value(item))));
      row.append(heading, progress(value(item), max));
      target.appendChild(row);
    }
  }

  function renderTrend(data) {
    const namespace = 'http://www.w3.org/2000/svg';
    const svgElement = (tag, attributes, content = '') => {
      const item = document.createElementNS(namespace, tag);
      for (const [key, value] of Object.entries(attributes)) item.setAttribute(key, value);
      item.textContent = content;
      return item;
    };
    const width = Math.max(360, $('[data-trend]').clientWidth);
    const height = document.body.classList.contains('is-bigscreen') ? 180 : 260;
    const svg = svgElement('svg', { viewBox: `0 0 ${width} ${height}`, role: 'img', 'aria-label': `从 ${data.from} 至 ${data.to} 的访问会话和下载请求趋势，采集前无数据` });
    const points = data.trend;
    const active = points.filter(point => point.collecting);
    const max = Math.max(4, ...active.flatMap(point => [point.sessions, point.downloads]));
    const ceiling = Math.ceil(max / 4) * 4;
    const x = index => points.length === 1 ? width / 2 : 45 + index * (width - 70) / (points.length - 1);
    const y = value => height - 42 - value / ceiling * (height - 70);
    for (let tick = 0; tick <= 4; tick += 1) {
      const value = ceiling * tick / 4;
      svg.append(svgElement('line', { x1: 45, y1: y(value), x2: width - 20, y2: y(value), class: 'chart-grid' }),
        svgElement('text', { x: 32, y: y(value) + 3, class: 'chart-label', 'text-anchor': 'end' }, fmt(value)));
    }
    points.forEach((point, index) => {
      if (index % Math.max(1, Math.ceil(points.length / 6)) === 0 || index === points.length - 1) {
        svg.appendChild(svgElement('text', { x: x(index), y: height - 15, class: 'chart-label', 'text-anchor': 'middle' }, point.day.slice(5)));
      }
    });
    for (const [key, css] of [['sessions', 'visits'], ['downloads', 'downloads']]) {
      const selected = points.map((point, index) => ({ ...point, index })).filter(point => point.collecting);
      if (selected.length) {
        const d = selected.map((point, index) => `${index ? 'L' : 'M'}${x(point.index)},${y(point[key])}`).join(' ');
        svg.appendChild(svgElement('path', { d, class: `chart-line chart-${css}` }));
        selected.forEach(point => {
          const circle = svgElement('circle', { cx: x(point.index), cy: y(point[key]), r: selected.length > 31 ? 2 : 3.5, class: `chart-point-${css}` });
          circle.appendChild(svgElement('title', {}, `${point.day} ${key === 'sessions' ? '访问会话' : '下载请求'}：${point[key]}`));
          svg.appendChild(circle);
        });
      }
    }
    if (!active.some(point => point.sessions || point.downloads)) {
      svg.appendChild(svgElement('text', { x: width / 2, y: height / 2 - 12, class: 'chart-empty', 'text-anchor': 'middle' }, '统计已启动，等待真实访问与下载'));
      svg.appendChild(svgElement('text', { x: width / 2, y: height / 2 + 13, class: 'chart-label', 'text-anchor': 'middle' }, '采集开始前的日期没有历史数据'));
    }
    $('[data-trend]').replaceChildren(svg);
    $('[data-trend-table]').replaceChildren(table(['日期', '页面浏览', '访问会话', '下载请求', '新增反馈'], points.map(point =>
      [point.day, ...['views', 'sessions', 'downloads', 'feedback'].map(key => point.collecting || key === 'feedback' && point[key] ? fmt(point[key]) : '未采集')])));
  }

  function renderOverview(data) {
    lastOverview = data;
    for (const card of document.querySelectorAll('[data-kpi]')) {
      const key = card.dataset.kpi;
      card.textContent = key === 'conversionRate' ? percent(data.totals[key]) : fmt(data.totals[key]);
    }
    $('[data-pending-badge]').textContent = fmt(data.totals.pending);
    $('[data-active]').textContent = fmt(data.totals.active);
    $('[data-live-version]').textContent = data.release.tag;
    $('[data-github-total]').textContent = fmt(data.github.total);
    $('[data-updated]').textContent = `更新于 ${time(data.generatedAt)} · ${data.from} — ${data.to}`;
    $('[data-overview-state]').textContent = data.analyticsErrorAt ? '采集曾发生写入错误，数据可能不完整，请检查服务器存储。' : data.health.feedbackImportWarning ? '部分历史反馈导入失败，请检查服务器上的反馈文件。' : '';
    renderTrend(data);
    const conversion = $('[data-conversion]');
    conversion.replaceChildren();
    for (const [label, value] of [['有页面浏览的会话', data.totals.sessions], ['其中发起下载的会话', data.totals.converted]]) {
      const step = element('div', '', 'conversion-step');
      const title = element('div');
      title.append(element('span', label), element('b', fmt(value)));
      step.append(title, progress(value, data.totals.sessions));
      conversion.appendChild(step);
    }
    conversion.appendChild(element('div', percent(data.totals.conversionRate), 'conversion-result'));
    distribution($('[data-sources]'), data.sources, row => `${sources[row.source] || row.source} · ${row.downloads} 个下载会话`, row => row.sessions, '用户同意匿名统计后开始记录来源。');
    distribution($('[data-categories]'), data.categories, row => categories[row.category] || row.category, row => row.count, '有新反馈后显示问题分布。');

    const versions = new Map(data.github.assets.map(asset => [asset.tag, { ...asset, redirects: 0 }]));
    for (const row of data.versions) {
      if (!versions.has(row.tag)) versions.set(row.tag, { tag: row.tag, downloads: null });
      versions.get(row.tag).redirects = row.count;
    }
    if (!versions.size) $('[data-versions]').replaceChildren(empty('等待版本数据', 'GitHub 同步成功后显示官方累计值。'));
    else $('[data-versions]').replaceChildren(table(['版本', '官网下载请求 · 所选范围', 'GitHub · 累计'], [...versions.values()].sort((a, b) => Number(b.tag === data.release.tag) - Number(a.tag === data.release.tag)).map(row => {
      const label = element('span', row.tag);
      if (row.tag === data.release.tag) label.appendChild(element('span', '当前', 'version-current'));
      return [label, fmt(row.redirects || 0), fmt(row.downloads)];
    })));
    $('[data-github-sync]').textContent = data.github.error || (data.github.syncedAt ? `GitHub 同步于 ${time(data.github.syncedAt)}；含所有渠道下载，每 15 分钟更新。` : 'GitHub 尚未同步，累计值暂不可用。');

    const recent = $('[data-recent-feedback]');
    recent.replaceChildren();
    if (!data.recentFeedback.length) recent.appendChild(empty('还没有反馈', '收到用户反馈后，可在这里跟进处理。'));
    for (const row of data.recentFeedback) {
      const item = element('div', '', 'feedback-item');
      const info = element('div');
      info.append(element('strong', `${row.game} · ${categories[row.category] || row.category}`), element('small', `${row.app_version} · ${time(row.at)}`));
      item.append(info, badge(row.status));
      recent.appendChild(item);
    }
    const audit = $('[data-audit]');
    audit.replaceChildren();
    const names = { release_published: '发布软件版本', release_publish_failed: '版本发布失败', admin_login_failed: '管理员登录失败', feedback_status_changed: '更新反馈状态' };
    if (!data.audit.length) audit.appendChild(empty('暂无管理操作记录', '从统计功能上线后开始记录。'));
    for (const row of data.audit.slice(0, 6)) {
      const item = element('div', '', 'audit-item');
      const info = element('div');
      info.append(element('strong', names[row.name] || row.name), element('small', row.name === 'feedback_status_changed' ? `${row.detail} · ${statuses[row.result] || row.result}` : row.detail || row.release_tag || '不记录密码或原始 IP'));
      item.append(info, element('time', time(row.at)));
      audit.appendChild(item);
    }
    const health = $('[data-health]');
    health.replaceChildren();
    for (const [label, value, note] of [
      ['服务端错误率', data.health.api.total ? percent(data.health.api.errors / data.health.api.total) : '—', `${fmt(data.health.api.errors)} 次 5xx / ${fmt(data.health.api.total)} 次关键请求`],
      ['接口耗时 P95', data.health.p95 === null ? '—' : `${fmt(data.health.p95)} ms`, '文件下载（含传输耗时）、反馈与发行'],
      ['进程运行时间', `${Math.floor(data.health.uptime / 3600)}h ${Math.floor(data.health.uptime % 3600 / 60)}m`, '容器重建后重新计时'],
      ['站点进程内存', `${Math.round(data.health.memoryBytes / 1048576)} MiB`, '不是整台服务器内存']
    ]) {
      const item = element('div', '', 'health-value');
      item.append(element('span', label), element('b', value), element('small', note));
      health.appendChild(item);
    }
    $('[data-collection-note]').textContent = `采集开始：${time(data.startedAt)}。事件明细保留 ${data.retentionDays} 天。拒绝统计、管理员和常见机器人不计入访问会话；下载总量含历史跳转及服务器下载请求，续传后续分段不重复计数，不能判断文件是否下载完成。`;
    const signals = [
      ['直接下载按钮点击', data.events.download_click || 0], ['开始填写反馈', data.events.feedback_start || 0],
      ['反馈保存成功', data.events.feedback_accepted || 0], ['反馈校验失败', data.events.feedback_rejected || 0],
      ['成功复制校验值', data.events.hash_copy || 0]
    ];
    $('[data-tracking-summary]').replaceChildren(table(['事件信号', `次数 · ${data.from} 至 ${data.to}`], signals));
  }

  async function loadOverview() {
    if (!authenticated) return;
    const id = ++requestId;
    overviewPending = true;
    $('[data-refresh]').disabled = true;
    try {
      const data = await api(`/api/admin/overview?days=${$('[data-range]').value}`);
      if (id === requestId && authenticated) renderOverview(data);
    } catch (error) {
      if (id === requestId) $('[data-overview-state]').textContent = `刷新失败，当前数值可能已过期：${error.message}`;
      if (error.status === 401) await window.muAdmin.refresh();
    } finally {
      if (id === requestId) { overviewPending = false; $('[data-refresh]').disabled = false; }
    }
  }

  function switchTab(name) {
    currentTab = name;
    for (const button of document.querySelectorAll('[data-tab]')) {
      if (button.dataset.tab === name) button.setAttribute('aria-current', 'page');
      else button.removeAttribute('aria-current');
    }
    for (const panel of document.querySelectorAll('[data-tab-panel]')) panel.hidden = panel.dataset.tabPanel !== name;
    history.replaceState(null, '', `#${name}`);
    window.dispatchEvent(new CustomEvent('mu:admin-tab', { detail: { name } }));
    if (name === 'feedback') loadFeedback();
    if (name === 'overview' || name === 'tracking') loadOverview();
  }

  async function loadFeedback() {
    if (!authenticated) return;
    $('[data-feedback-message]').textContent = '正在读取反馈…';
    try {
      const data = await api(`/api/admin/feedback?status=${$('[data-feedback-filter]').value}&page=${feedbackPage}`);
      if (!authenticated) return;
      feedbackPage = data.page;
      feedbackPages = data.pages;
      $('[data-feedback-message]').textContent = '';
      $('[data-feedback-page]').textContent = `共 ${data.total} 条 · 第 ${data.page} / ${data.pages} 页`;
      $('[data-feedback-prev]').disabled = data.page <= 1;
      $('[data-feedback-next]').disabled = data.page >= data.pages;
      if (!data.items.length) return $('[data-feedback-table]').replaceChildren(empty('没有符合条件的反馈', '新反馈会自动出现在这里。'));
      $('[data-feedback-table]').replaceChildren(table(['收到时间', '类型 / 版本', '游戏', '显卡', '状态', '操作'], data.items.map(row => {
        const button = element('button', '查看详情', 'row-action');
        button.type = 'button';
        button.addEventListener('click', () => openFeedback(row));
        return [time(row.at), `${categories[row.category] || row.category} / ${row.app_version}`, row.game, row.gpu, badge(row.status), button];
      })));
    } catch (error) {
      $('[data-feedback-message]').textContent = error.message;
      if (error.status === 401) await window.muAdmin.refresh();
    }
  }

  function openFeedback(row) {
    selectedFeedback = row;
    const detail = element('dl');
    const payload = row.payload;
    for (const [key, value] of [['编号', row.id], ['提交时间', time(row.at)], ['软件版本', row.app_version], ['游戏', row.game], ['显卡 / 驱动', `${row.gpu} / ${payload.driver || '未填写'}`], ['模式 / 分辨率', `${row.mode} / ${payload.resolution || '未填写'}`], ['用户自报 FPS', `${payload.fpsBefore || '—'} → ${payload.fpsAfter || '—'}`], ['详细说明', payload.details], ['联系方式', payload.contact || '未提供']]) {
      detail.append(element('dt', key), element('dd', value || '—'));
    }
    $('[data-feedback-detail]').replaceChildren(detail);
    $('[data-feedback-status-form]').elements.status.value = row.status;
    $('[data-dialog-message]').textContent = '';
    $('[data-feedback-dialog]').showModal();
  }

  document.querySelectorAll('[data-tab]').forEach(button => button.addEventListener('click', () => switchTab(button.dataset.tab)));
  $('[data-open-feedback]').addEventListener('click', () => switchTab('feedback'));
  $('[data-range]').addEventListener('change', loadOverview);
  $('[data-refresh]').addEventListener('click', loadOverview);
  $('[data-feedback-filter]').addEventListener('change', () => { feedbackPage = 1; loadFeedback(); });
  $('[data-feedback-prev]').addEventListener('click', () => { feedbackPage = Math.max(1, feedbackPage - 1); loadFeedback(); });
  $('[data-feedback-next]').addEventListener('click', () => { feedbackPage = Math.min(feedbackPages, feedbackPage + 1); loadFeedback(); });
  $('[data-dialog-close]').addEventListener('click', () => $('[data-feedback-dialog]').close());
  $('[data-feedback-status-form]').addEventListener('submit', async event => {
    event.preventDefault();
    if (!selectedFeedback) return;
    const form = event.currentTarget;
    const button = form.querySelector('button');
    button.disabled = true;
    try {
      await api('/api/admin/feedback/status', { id: selectedFeedback.id, status: form.elements.status.value });
      $('[data-dialog-message]').textContent = '处理状态已保存。';
      await loadFeedback();
      loadOverview();
    } catch (error) { $('[data-dialog-message]').textContent = error.message; }
    finally { button.disabled = false; }
  });
  $('[data-screen]').addEventListener('click', async () => {
    if (document.body.classList.contains('is-bigscreen')) {
      if (document.fullscreenElement) await document.exitFullscreen();
      document.body.classList.remove('is-bigscreen');
    } else {
      switchTab('overview');
      document.body.classList.add('is-bigscreen');
      try { await document.documentElement.requestFullscreen(); } catch { /* A wide layout also works without Fullscreen permission. */ }
    }
    $('[data-screen]').textContent = document.body.classList.contains('is-bigscreen') ? '退出大屏' : '大屏模式';
    if (lastOverview) renderTrend(lastOverview);
  });
  document.addEventListener('fullscreenchange', () => {
    if (!document.fullscreenElement) { document.body.classList.remove('is-bigscreen'); $('[data-screen]').textContent = '大屏模式'; }
    if (lastOverview) renderTrend(lastOverview);
  });
  window.addEventListener('mu:admin-session', event => {
    authenticated = event.detail.ok;
    $('[data-screen]').hidden = !authenticated;
    if (authenticated) switchTab(currentTab);
    else {
      requestId += 1;
      $('[data-feedback-dialog]').close();
      selectedFeedback = null;
      lastOverview = null;
      $('[data-feedback-detail]').replaceChildren();
      $('[data-feedback-table]').replaceChildren();
      $('[data-recent-feedback]').replaceChildren();
      document.body.classList.remove('is-bigscreen');
    }
  });
  window.addEventListener('mu:release-updated', loadOverview);
  let resizeTimer;
  window.addEventListener('resize', () => {
    clearTimeout(resizeTimer);
    resizeTimer = setTimeout(() => { if (lastOverview && currentTab === 'overview') renderTrend(lastOverview); }, 120);
  });
  setInterval(() => {
    if (authenticated && currentTab === 'overview' && !overviewPending && $('[data-auto-refresh]').checked && document.visibilityState === 'visible') loadOverview();
  }, 30000);
  const updateClock = () => { $('[data-clock]').textContent = new Date().toLocaleTimeString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false }); };
  updateClock();
  setInterval(updateClock, 1000);
})();
