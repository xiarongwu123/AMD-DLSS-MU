(() => {
  const $ = selector => document.querySelector(selector);
  const fmt = value => Number(value).toLocaleString('zh-CN');
  const percent = value => value === null ? '—' : `${(value * 100).toFixed(1)}%`;
  const time = value => new Date(value).toLocaleString('zh-CN', { timeZone: 'Asia/Shanghai', hour12: false });
  let authenticated = false;
  let page = 1;
  let pages = 1;
  let sequence = 0;
  let pending = false;
  function el(tag, text = '', className = '') {
    const node = document.createElement(tag); node.textContent = text;
    if (className) node.className = className;
    return node;
  }
  function bar(label, count, ratio, suffix = '') {
    const row = el('div', '', 'distribution-row');
    const heading = el('div', '', 'distribution-label');
    heading.append(el('span', label), el('b', `${fmt(count)} 票 · ${percent(ratio)}`));
    const progress = el('progress'); progress.max = 1; progress.value = ratio;
    progress.setAttribute('aria-label', `${label}：${fmt(count)} 票，${percent(ratio)}`);
    row.append(heading, progress);
    if (suffix) row.append(el('small', suffix, 'survey-first-votes'));
    return row;
  }
  function detail(row, names, payments) {
    const list = el('dl');
    for (const [key, value] of [['答卷编号', row.id], ['问卷版本', row.version], ['首次提交', time(row.created_at)], ['最近修改', time(row.updated_at)],
      ['已选功能', row.features.map(id => names[id]).join('、')], ['首选功能', names[row.priority] || '不适用'],
      ['付费意愿', payments[row.payment]], ['建议', row.suggestion || '未填写']]) list.append(el('dt', key), el('dd', value));
    $('[data-survey-detail]').replaceChildren(list);
    $('[data-survey-dialog]').showModal();
  }
  function render(data) {
    const names = Object.fromEntries(data.ranking.map(item => [item.id, item.name])); names.none = '暂时没有感兴趣的功能';
    const payments = Object.fromEntries(data.payments.map(item => [item.id, item.name]));
    page = data.page; pages = data.pages;
    $('[data-survey-total]').textContent = fmt(data.total);
    $('[data-survey-leading]').textContent = data.ranking[0]?.votes ? data.ranking[0].name : '暂无功能投票';
    $('[data-survey-willing]').textContent = percent(data.willingRatio);
    $('[data-survey-ranking]').replaceChildren(...data.ranking.map((item, index) => bar(`${String(index + 1).padStart(2, '0')} / ${item.name}`, item.votes, item.ratio, `首选 ${fmt(item.priorityVotes)} 票`)));
    $('[data-survey-none]').textContent = `暂时没有感兴趣的功能：${fmt(data.none)} 份（${percent(data.total ? data.none / data.total : 0)}）。`;
    $('[data-survey-payment-chart]').replaceChildren(...data.payments.map(item => bar(item.name, item.count, item.ratio)));
    const table = el('table');
    const head = el('thead'); const heading = el('tr');
    ['首次提交 / 最近修改', '已选功能', '首选功能', '付费意愿', '详情'].forEach(text => heading.append(el('th', text)));
    head.append(heading); const body = el('tbody');
    for (const row of data.items) {
      const tr = el('tr');
      const button = el('button', '查看答卷', 'row-action'); button.type = 'button';
      button.addEventListener('click', () => detail(row, names, payments));
      for (const value of [`${time(row.created_at)}\n${time(row.updated_at)}`, row.features.map(id => names[id]).join('、'), names[row.priority] || '不适用', payments[row.payment], button]) {
        const td = el('td'); if (value instanceof Node) td.append(value); else td.textContent = value; tr.append(td);
      }
      body.append(tr);
    }
    table.append(head, body);
    $('[data-survey-table]').replaceChildren(data.total ? table : el('div', '当前范围还没有答卷。收到回答后将在此显示。', 'empty-state'));
    $('[data-survey-page]').textContent = `共 ${fmt(data.total)} 份 · 第 ${page} / ${pages} 页`;
    $('[data-survey-prev]').disabled = page <= 1; $('[data-survey-next]').disabled = page >= pages;
    $('[data-survey-updated]').textContent = `更新于 ${time(data.generatedAt)} · 匿名浏览器不等于独立人数；清除 Cookie 或换设备可能重复填写。答卷持久保存，不随访问统计清理。`;
    $('[data-survey-report]').hidden = false;
  }
  async function load() {
    if (!authenticated) return;
    const id = ++sequence;
    pending = true; $('[data-survey-refresh]').disabled = true;
    $('[data-survey-report]').hidden = true;
    $('[data-survey-message]').textContent = '正在读取问卷统计…';
    $('[data-survey-dialog]').close();
    try {
      const data = await window.muAdmin.api(`/api/admin/survey?days=${$('[data-survey-range]').value}&page=${page}`);
      if (!authenticated || id !== sequence) return;
      render(data); $('[data-survey-message]').textContent = '';
    } catch (error) {
      if (id === sequence && authenticated) $('[data-survey-message]').textContent = `读取失败：${error.message}`;
      if (error.status === 401) await window.muAdmin.refresh();
    } finally { if (id === sequence) { pending = false; $('[data-survey-refresh]').disabled = false; } }
  }
  $('[data-survey-refresh]').addEventListener('click', load);
  $('[data-survey-range]').addEventListener('change', () => { page = 1; load(); });
  $('[data-survey-prev]').addEventListener('click', () => { page = Math.max(1, page - 1); load(); });
  $('[data-survey-next]').addEventListener('click', () => { page = Math.min(pages, page + 1); load(); });
  $('[data-survey-close]').addEventListener('click', () => $('[data-survey-dialog]').close());
  window.addEventListener('mu:admin-tab', event => {
    if (event.detail.name === 'survey') load();
    else { sequence += 1; pending = false; $('[data-survey-dialog]').close(); }
  });
  window.addEventListener('mu:admin-session', event => {
    authenticated = event.detail.ok;
    if (authenticated && location.hash === '#survey') load();
    if (!authenticated) {
      sequence += 1; pending = false; page = 1;
      $('[data-survey-dialog]').close(); $('[data-survey-report]').hidden = true;
      for (const selector of ['[data-survey-detail]', '[data-survey-ranking]', '[data-survey-payment-chart]', '[data-survey-table]']) $(selector).replaceChildren();
      for (const selector of ['[data-survey-total]', '[data-survey-leading]', '[data-survey-willing]', '[data-survey-none]', '[data-survey-page]', '[data-survey-updated]', '[data-survey-message]']) $(selector).textContent = '';
    }
  });
  setInterval(() => {
    if (authenticated && location.hash === '#survey' && !pending && document.visibilityState === 'visible' && !$('[data-survey-dialog]').open) load();
  }, 30000);
})();
