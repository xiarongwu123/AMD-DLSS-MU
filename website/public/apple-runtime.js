(() => {
  'use strict';
  const one = selector => document.querySelector(selector);
  const make = (tag, text, className) => {
    const element = document.createElement(tag);
    if (text) element.textContent = text;
    if (className) element.className = className;
    return element;
  };
  const request = async path => {
    const response = await fetch(path, { credentials: 'same-origin', cache: 'no-store', signal: AbortSignal.timeout(12000) });
    if (!response.ok) throw new Error('HTTP ' + response.status);
    return response.json();
  };

  request('/release.json').then(release => {
    const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_TEXT);
    while (walker.nextNode()) {
      const node = walker.currentNode;
      if (['SCRIPT', 'STYLE'].includes(node.parentElement.tagName)) continue;
      if (node.parentElement.closest('.timeline')) continue;
      node.textContent = node.textContent.replace(/2\.0\.7/g, release.version)
        .replace(/191\.8 MiB/g, release.sizeDisplay)
        .replace(/\b[a-f0-9]{64}\b/g, release.sha256);
    }
    document.querySelectorAll('[data-copy]').forEach(button => {
      if (/^[a-f0-9]{64}$/.test(button.dataset.copy)) button.dataset.copy = release.sha256;
    });
    if (one('body[data-page="download"] .timeline')) {
      const row = make('div', '', 'release-row');
      const content = make('div');
      content.append(make('h4', '当前正式版'), make('pre', release.notes, 'apple-release-notes'));
      row.append(make('time', release.tag), content);
      one('.timeline').prepend(row);
    }
    const versionField = one('[name="appVersion"]');
    if (versionField && !versionField.value) versionField.value = release.version;
  }).catch(() => {});

  const surveyButton = one('#submitSurvey');
  if (surveyButton) {
    let session;
    const initialize = () => session ||= request('/api/survey').catch(error => { session = null; throw error; });
    initialize().catch(() => {});
    const submit = surveyButton.onclick;
    surveyButton.onclick = async event => {
      try {
        await initialize();
        await submit.call(surveyButton, event);
      } catch {
        one('#surveyStatus').textContent = '问卷会话暂时无法初始化，请稍后重试。填写内容已保留。';
      }
    };
  }

  const results = one('#liveResults');
  if (!results) return;
  const detail = make('section', '', 'apple-game-detail');
  detail.id = 'game-detail';
  results.after(detail);
  let current;
  const hardware = request('/assets/hardware-reference.json').catch(() => null);
  const profile = () => ({ gpu: one('#gpu').value, cpu: one('#cpu').value, ramGb: Number(one('#ram').value) || undefined });
  const render = async data => {
    const database = await hardware;
    if (current !== data) return;
    detail.replaceChildren(make('h3', data.game.name));
    if (window.MuHardwareCheck && database) {
      const comparison = window.MuHardwareCheck.evaluate(data.requirements, profile(), database);
      detail.append(make('h4', comparison.title), make('p', comparison.summary));
      comparison.checks.forEach(check => detail.append(make('p', check.label + '：' + check.text)));
      one('#tierState').textContent = comparison.title;
    }
    for (const [key, title] of [['minimum', '最低配置'], ['recommended', '推荐配置']]) {
      const tier = data.requirements?.[key];
      const box = make('section', '', 'apple-requirement-tier');
      box.append(make('h4', title));
      if (!tier) box.append(make('p', '官方资料未提供，暂时无法判断。'));
      else {
        for (const [field, label] of [['os', '系统'], ['processor', '处理器'], ['memory', '内存'], ['graphics', '显卡'], ['directX', 'DirectX'], ['storage', '存储空间']]) {
          if (tier[field]) box.append(make('p', label + '：' + tier[field]));
        }
        const original = make('details');
        original.append(make('summary', '查看官方原文'), make('pre', tier.text, 'apple-release-notes'));
        box.append(original);
      }
      detail.append(box);
    }
    detail.append(make('h4', 'OptiScaler 上游适配资料'));
    if (!data.modCompatibility?.length) detail.append(make('p', '尚未收录上游条目，适配状态未知。'));
    for (const adaptation of data.modCompatibility || []) {
      detail.append(make('p', adaptation.status + ' · ' + (adaptation.notes || []).join('；')));
      if (adaptation.sourceUrl) {
        const link = make('a', '查看上游原始记录 ↗');
        link.href = adaptation.sourceUrl;
        link.target = '_blank';
        link.rel = 'noopener noreferrer';
        detail.append(link);
      }
    }
    detail.append(make('p', '历史用户实测：' + (data.counts?.total || 0) + ' 条。硬件估算和上游记录不代表 MU 或 DLSS5 已通过实测。'));
    for (const test of data.tests || []) {
      detail.append(make('p', [test.result, test.environment?.gpu?.name, test.environment?.gpu?.driverVersion,
        test.environment?.gameVersion, test.environment?.toolVersion, test.environment?.osVersion].filter(Boolean).join(' · ')));
    }
  };
  results.addEventListener('click', async event => {
    const link = event.target.closest('a[data-game-id]');
    if (!link) return;
    event.preventDefault();
    detail.textContent = '正在读取官方配置与适配记录…';
    const id = link.dataset.gameId;
    detail.dataset.pending = id;
    try {
      const data = await request('/api/compatibility/games/' + encodeURIComponent(id));
      if (detail.dataset.pending !== id) return;
      current = data;
      await render(data);
      detail.scrollIntoView({ behavior: 'smooth', block: 'start' });
    } catch { detail.textContent = '详细资料暂时不可用，请稍后重试。'; }
  });
  ['#gpu', '#cpu', '#ram'].forEach(selector => one(selector).addEventListener('input', () => { if (current) render(current); }));
  new MutationObserver(() => {
    for (const link of results.querySelectorAll('a:not([data-game-id])')) link.remove();
  }).observe(results, { childList: true });
})();
