(() => {
  'use strict';
  const byId = id => document.getElementById(id);
  const form = byId('compat-search');
  if (!form) return;
  const query = byId('compat-q');
  const gpu = byId('compat-gpu');
  const profileInputs = Object.fromEntries(['gpu', 'cpu', 'ram', 'vram', 'os', 'dx', 'storage'].map(key => [key, byId('compat-' + key)]));
  const games = byId('compat-games');
  const detail = byId('compat-detail');
  const content = byId('compat-detail-content');
  const searchState = byId('compat-search-state');
  const detailState = byId('compat-detail-state');
  const searchRetry = byId('compat-search-retry');
  const detailRetry = byId('compat-detail-retry');
  const share = byId('compat-share');
  const pageSize = 20;
  const pagination = byId('compat-pagination');
  let pageCount = 1;
  let catalogTotal;
  const labels = { success: '成功实测', partial: '部分成功', failure: '失败实测', mixed: '结果不一致', untested: '待验证' };
  const reasons = { startup_crash: '启动或进入游戏崩溃', load_failed: 'DLL 无法加载', menu_missing: 'DLSS 菜单未出现 / 功能未生效', game_update: '游戏更新后失效', anti_cheat: '反作弊拦截', visual_artifacts: 'UI 异常 / 画面闪烁', performance: '性能下降', unknown: '未知 / 其他问题' };
  const modLabels = { working: '上游记录可用', not_working: '上游记录不兼容', platform_limited: '有平台限制', mixed: '不同环境结论不同' };
  let state = { q: '', gpu: '', cpu: '', ram: '', vram: '', os: '', dx: '', storage: '', game: '', page: 1 };
  let hardwareDatabase, loadedDetail;
  let searchController, detailController;
  let searchGeneration = 0, detailGeneration = 0, gpuGeneration = 0;

  function node(tag, className, text) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = String(text);
    return element;
  }
  function message(element, text, kind = 'ready') {
    element.textContent = text;
    element.dataset.state = kind;
  }
  function known(value) {
    return typeof value === 'string' && value.trim() && value.toLowerCase() !== 'unknown' ? value : '未知';
  }
  function time(value) {
    if (!value) return '时间未知';
    const date = new Date(value);
    return Number.isNaN(date.getTime()) ? '时间未知' : new Intl.DateTimeFormat('zh-CN', { dateStyle: 'medium', timeStyle: 'short' }).format(date);
  }
  function counts(value) {
    if (!value || !['success', 'partial', 'failure'].every(key => Number.isSafeInteger(value[key]) && value[key] >= 0)) throw new Error('invalid_response');
    return { ...value, total: value.success + value.partial + value.failure };
  }
  function status(value, count) {
    if (!Object.hasOwn(labels, value)) throw new Error('invalid_response');
    return count.total === 0 ? 'untested' : value;
  }
  function badge(value) {
    const element = node('span', 'compat-status', labels[value]);
    element.dataset.result = value;
    return element;
  }
  function errorText(error) {
    if (error.status === 404) return '未找到这款游戏，记录可能已更新。请重新搜索游戏。';
    if (error.status === 400) return '查询参数无效，请检查游戏名称和显卡型号后重试。';
    if (error.status === 403) return '兼容查询暂未开放，请稍后重试。';
    if (error.status === 429) return '查询较频繁，请稍后重试。';
    return '配置与适配资料暂时无法加载，请稍后重试。';
  }
  async function fetchJson(path, controller) {
    const timeout = setTimeout(() => controller.abort('timeout'), 15000);
    try {
      const response = await fetch(path, { signal: controller.signal, headers: { Accept: 'application/json' }, cache: 'no-store', credentials: 'omit', referrerPolicy: 'no-referrer' });
      if (!response.ok) {
        const error = new Error('request_failed');
        error.status = response.status;
        throw error;
      }
      return await response.json();
    } finally { clearTimeout(timeout); }
  }
  function writeUrl(mode = 'push') {
    const url = new URL(location.href);
    for (const key of ['q', 'gpu', 'cpu', 'ram', 'vram', 'os', 'dx', 'storage', 'game']) {
      if (state[key]) url.searchParams.set(key, state[key]);
      else url.searchParams.delete(key);
    }
    if (state.page > 1) url.searchParams.set('page', String(state.page));
    else url.searchParams.delete('page');
    if (url.href !== location.href) history[mode === 'replace' ? 'replaceState' : 'pushState'](null, '', url);
    byId('compat-share-state').textContent = '';
    byId('compat-share-fallback').hidden = true;
  }
  function markSelection() {
    games.querySelectorAll('button[data-game]').forEach(button => button.setAttribute('aria-pressed', String(button.dataset.game === state.game)));
  }
  function resetDetail() {
    loadedDetail = undefined;
    content.replaceChildren();
    detailRetry.hidden = true;
    share.hidden = true;
    byId('compat-detail-title').textContent = '配置与适配详情';
    document.title = '我的电脑能玩吗 · AMD DLSS MU';
    byId('compat-share-state').textContent = '';
    byId('compat-share-fallback').hidden = true;
  }
  async function search({ game = '', page = 1, historyMode = 'push', focusDetail = false, focusCatalog = false } = {}) {
    const generation = ++searchGeneration;
    searchController?.abort();
    detailController?.abort();
    ++detailGeneration;
    state = { q: query.value.trim(), ...readProfile(), game, page };
    if (historyMode) writeUrl(historyMode);
    games.replaceChildren();
    games.setAttribute('aria-busy', 'true');
    searchRetry.hidden = true;
    pagination.hidden = true;
    resetDetail();
    detail.setAttribute('aria-busy', 'false');
    message(searchState, '正在查询游戏目录与资料覆盖…', 'loading');
    message(detailState, '选择一个游戏，查看官方要求与硬件对比。');
    const controller = searchController = new AbortController();
    if (game) void loadDetail(game, focusDetail);
    try {
      const params = new URLSearchParams({ q: state.q, page: String(state.page), pageSize: String(pageSize) });
      const data = await fetchJson('/api/compatibility/games?' + params, controller);
      if (generation !== searchGeneration) return;
      if (!data || !Array.isArray(data.items) || ![data.total, data.page, data.pageSize, data.catalogTotal].every(Number.isSafeInteger)
        || data.page !== state.page || data.pageSize !== pageSize || data.total < 0 || data.catalogTotal < data.total) throw new Error('invalid_response');
      pageCount = Math.max(1, Math.ceil(data.total / data.pageSize));
      catalogTotal = data.catalogTotal;
      const coverageNumber = key => Number.isSafeInteger(data.coverage?.[key]) && data.coverage[key] >= 0 && data.coverage[key] <= catalogTotal ? data.coverage[key].toLocaleString('zh-CN') : '—';
      byId('compat-catalog-count').textContent = `${catalogTotal.toLocaleString('zh-CN')} 款游戏目录 · ${coverageNumber('requirements')} 款官方配置 · ${coverageNumber('modCompatibility')} 款上游适配资料`;
      if (state.page > pageCount) { void search({ game, page: pageCount, historyMode: 'replace', focusCatalog }); return; }
      const fragment = document.createDocumentFragment();
      for (const item of data.items.slice(0, 50)) {
        if (typeof item.game?.id !== 'string' || typeof item.game?.name !== 'string') throw new Error('invalid_response');
        const button = node('button', 'compat-game-button');
        button.type = 'button';
        button.dataset.game = item.game.id;
        button.setAttribute('aria-controls', 'compat-detail');
        button.append(node('span', 'compat-game-name', item.game.name));
        if (item.game.catalog?.localizedName && item.game.catalog.localizedName !== item.game.name)
          button.append(node('span', 'compat-game-localized', item.game.catalog.localizedName));
        const meta = node('span', 'compat-game-meta');
        const official = node('span', 'compat-evidence', item.evidence?.hasRequirements ? '官方配置已收录' : '查看官方商店资料');
        official.dataset.available = String(Boolean(item.evidence?.hasRequirements));
        meta.append(official);
        if (modLabels[item.evidence?.modStatus]) meta.append(node('span', '', 'OptiScaler · ' + modLabels[item.evidence.modStatus]));
        button.append(meta);
        button.addEventListener('click', () => {
          state.game = item.game.id;
          writeUrl();
          markSelection();
          void loadDetail(item.game.id, true);
        });
        const row = node('li');
        row.append(button);
        fragment.append(row);
      }
      games.replaceChildren(fragment);
      games.scrollTop = 0;
      markSelection();
      message(searchState, data.items.length
        ? `找到 ${data.total.toLocaleString('zh-CN')} 款游戏。当前显示第 ${(state.page - 1) * pageSize + 1}–${(state.page - 1) * pageSize + data.items.length} 款。`
        : state.q ? '没有匹配的游戏。试试其他名称、Steam App ID，或清空关键词查看目录。' : '游戏目录暂未收录游戏。', data.items.length ? 'ready' : 'empty');
      byId('compat-page-state').textContent = `${state.page} / ${pageCount}`;
      byId('compat-page-number').value = state.page;
      byId('compat-page-number').max = pageCount;
      byId('compat-prev').disabled = state.page <= 1;
      byId('compat-next').disabled = state.page >= pageCount;
      pagination.hidden = pageCount <= 1;
      if (focusCatalog) byId('compat-catalog-title').focus();
    } catch (error) {
      if (generation !== searchGeneration) return;
      if (catalogTotal === undefined) byId('compat-catalog-count').textContent = '游戏目录规模暂时无法读取，请稍后重试。';
      message(searchState, errorText(error), 'error');
      searchRetry.hidden = false;
    } finally {
      if (generation === searchGeneration) games.setAttribute('aria-busy', 'false');
    }
  }
  function appendPair(list, title, value) {
    list.append(node('dt', '', title), node('dd', '', value));
  }
  function sourceLink(title, url) {
    const target = new URL(url);
    const referenceUrls = [
      'https://www.nvidia.com/en-us/geforce/news/nvidia-rtx-games-engines-apps/',
      'https://www.nvidia.com/en-us/geforce/graphics-cards/compare/',
      'https://www.intel.com/content/www/us/en/products/sku/241598/intel-arc-b580-graphics/specifications.html',
      'https://www.intel.com/content/www/us/en/products/sku/241676/intel-arc-b570-graphics/specifications.html',
      'https://www.tomshardware.com/reviews/gpu-hierarchy,4388.html',
      'https://www.tomshardware.com/reviews/gpu-hierarchy,4388-2.html',
      'https://www.tomshardware.com/reviews/cpu-hierarchy,4312.html',
      'https://www.tomshardware.com/reviews/cpu-hierarchy,4312-2.html',
      'https://www.amd.com/en/products/graphics/desktops/radeon/9000-series/amd-radeon-rx-9070xt.html',
      'https://www.amd.com/en/products/graphics/desktops/radeon/9000-series/amd-radeon-rx-9070.html',
      'https://www.amd.com/en/products/graphics/desktops/radeon/9000-series/amd-radeon-rx-9070-gre.html',
      'https://www.xfxforce.com/gpus/xfx-amd-radeon-tm-rx-5600-xt-6gb-gddr6-raw-ii'
    ];
    if (!/^https:\/\/store\.steampowered\.com\/app\/[1-9]\d*\/$/u.test(target.href)
      && !/^https:\/\/github\.com\/optiscaler\/OptiScaler\/wiki\/[^/?#]+\/[a-f0-9]{40}$/u.test(target.href)
      && !referenceUrls.includes(target.href)) throw new Error('invalid_response');
    const link = node('a', 'compat-source-link', title);
    link.href = target.href; link.target = '_blank'; link.rel = 'noopener noreferrer';
    return link;
  }
  function renderCatalog(game, fragment) {
    const catalog = game.catalog;
    if (!catalog) return;
    const box = node('details', 'compat-source-box');
    box.append(node('summary', '', '游戏身份与图形技术资料'));
    if (catalog.localizedName && catalog.localizedName !== game.name) box.append(node('p', '', '商店中文名称：' + catalog.localizedName));
    if (catalog.releaseDate) box.append(node('p', '', 'Steam 目录标注日期：' + catalog.releaseDate));
    box.append(sourceLink('查看 Steam 官方商店资料 ↗', catalog.url), node('p', 'compat-hint', '资料核对：' + time(catalog.retrievedAt)));
    for (const reference of catalog.references) {
      const item = node('div', 'compat-reference');
      item.append(node('strong', '', reference.provider), node('p', '', reference.features.join(' · ')),
        node('p', 'compat-hint', '来源条目：' + reference.matchedTitle + ' · 核对于 ' + time(reference.retrievedAt)),
        sourceLink('查看技术资料来源 ↗', reference.url));
      box.append(item);
    }
    box.append(node('p', 'compat-hint', catalog.references.length
      ? '厂商列表列出游戏内置或 NVIDIA App 可覆盖的图形功能，使用条件见原表；AMD 显卡上的替换方案请看上游适配资料。'
      : '当前已核对游戏身份；尚未收录可确认的渲染 API / DLSS 技术资料，保留未知。'));
    fragment.append(box);
  }
  function readProfile() {
    return Object.fromEntries(Object.entries(profileInputs).map(([key, input]) => [key, input.checkValidity() ? input.value.trim() : '']));
  }
  function renderHardware(requirements, fragment) {
    const box = node('section', 'compat-hardware-result');
    box.setAttribute('aria-label', '我的电脑配置对比');
    if (!hardwareDatabase || !window.MuHardwareCheck) {
      box.append(node('h3', '', '硬件对比参考库暂未就绪'), node('p', '', '你仍可以查看下方官方最低与推荐配置；参考库加载完成后会自动对比。'));
    } else {
      const model = window.MuHardwareCheck.matchHardware(state.gpu, hardwareDatabase.gpu);
      const result = window.MuHardwareCheck.evaluate(requirements, { gpu: state.gpu, cpu: state.cpu, ramGb: state.ram, vramGb: model?.vramMb ? undefined : state.vram }, hardwareDatabase);
      box.dataset.verdict = result.status;
      box.append(node('p', 'compat-kicker', 'YOUR PC / 配置档位估算'), node('h3', '', result.title), node('p', 'compat-hint', result.summary));
      const checks = node('ul', 'compat-check-list');
      for (const check of result.checks) {
        const row = node('li'); row.dataset.verdict = check.status;
        row.append(node('strong', '', check.label + ' · ' + ({ pass: '达到参考', fail: '低于参考', unknown: '待补充资料' }[check.status] || '未知')), node('p', '', check.text));
        if (check.note) row.append(node('p', 'compat-hint', check.note));
        checks.append(row);
      }
      box.append(checks);
      if (result.sources.length) {
        const sources = node('details', 'compat-benchmark-sources'); sources.append(node('summary', '', '查看本次硬件估算依据'));
        for (const source of result.sources) sources.append(sourceLink(source.provider + ' · ' + (source.label || '硬件参考') + ' ↗', source.url), node('p', 'compat-hint', '资料核对：' + time(source.retrievedAt)));
        box.append(sources);
      }
    }
    if (state.os || state.dx || state.storage) {
      const manual = node('p', 'compat-hint', '你填写的安装环境：' + [state.os, state.dx, state.storage ? '可用空间 ' + state.storage + ' GB' : ''].filter(Boolean).join(' · ') + '。这些项目未自动判定，请对照下方系统、DirectX 与存储要求。');
      box.append(manual);
    }
    fragment.append(box);
  }
  function renderRequirements(requirements, fragment) {
    const section = node('section', 'compat-requirements');
    section.append(node('h3', 'compat-block-title', 'Steam 官方配置要求'));
    if (!requirements || requirements.status !== 'available') {
      section.append(node('p', 'compat-hint', requirements?.status === 'not_provided' ? '当前 Steam 商店资料未提供 Windows 配置要求，请通过下方商店来源核对。' : '这款游戏的官方配置尚未取得或仍在更新中，暂时无法作硬件判断。'));
    } else {
      const tiers = node('div', 'compat-requirement-tiers');
      for (const [key, label] of [['minimum', '最低配置'], ['recommended', '推荐配置']]) {
        const tier = requirements[key]; const box = node('section', 'compat-requirement-tier'); box.append(node('h4', '', label));
        if (!tier) box.append(node('p', 'compat-hint', '此项未单独提供结构化资料，请查看完整官方原文。'));
        else {
          const fields = node('dl', 'compat-requirement-fields');
          for (const [field, title] of [['os', '操作系统'], ['processor', '处理器'], ['memory', '内存'], ['graphics', '显卡'], ['directX', 'DirectX'], ['storage', '存储空间'], ['additionalNotes', '其他要求']]) appendPair(fields, title, tier[field] || '官方资料未明确 / 未能结构化识别');
          const original = node('details', 'compat-original'); original.append(node('summary', '', '查看完整官方原文'), node('p', '', tier.text || '原文未提供'));
          box.append(fields, original);
        }
        tiers.append(box);
      }
      section.append(tiers);
    }
    if (requirements?.sourceUrl) section.append(sourceLink('查看 Steam 官方要求 ↗', requirements.sourceUrl), node('p', 'compat-hint', '采集时间：' + time(requirements.sourceRetrievedAt) + ' · 原文由发行商提供；配置与游戏版本可能更新。'));
    fragment.append(section);
  }
  function renderAdaptations(adaptations, fragment) {
    const section = node('section', 'compat-adaptations');
    section.append(node('h3', 'compat-block-title', 'OptiScaler 上游适配资料'));
    if (!Array.isArray(adaptations) || !adaptations.length) section.append(node('p', 'compat-hint', '尚未收录该游戏的上游适配条目，Mod 适配状态保留未知。'));
    else for (const item of adaptations) {
      const box = node('article', 'compat-adaptation'); box.dataset.verdict = item.status;
      box.append(node('h4', '', modLabels[item.status] || '适配状态未知'));
      const modRequirement = { none: '无需额外前置 Mod（仍需 OptiScaler）', third_party_upscaler: '需第三方超分 Mod', luma_ue: '需 Luma Unreal Engine Mod' }[item.requiredMod];
      if (item.requiredMod) box.append(node('p', '', '所需组件：' + (modRequirement || item.requiredMod)));
      if (item.upscalerInputs?.length) box.append(node('p', '', '上游列出的游戏输入：' + item.upscalerInputs.join(' / ')));
      if (item.notes?.length) { const notes = node('ul'); item.notes.forEach(note => notes.append(node('li', '', note))); box.append(notes); }
      if (item.testEnvironment) box.append(node('p', 'compat-hint', '上游记录环境：' + [['OptiScaler', item.testEnvironment.optiscalerVersion], ['GPU', item.testEnvironment.gpu], ['系统', item.testEnvironment.os]].map(([key, value]) => key + ' ' + known(value)).join(' · ')));
      box.append(sourceLink('查看 ' + item.sourceProvider + ' 原始记录 ↗', item.sourceUrl), node('p', 'compat-hint', '资料核对：' + time(item.sourceRetrievedAt)));
      section.append(box);
    }
    section.append(node('p', 'compat-hint', '上游适配记录与硬件配置判断相互独立；请按记录中的平台、版本和限制使用。此处不表示 MU 或 DLSS5 已通过验证。'));
    fragment.append(section);
  }
  function renderDetail(data) {
    if (typeof data.game?.name !== 'string' || !Array.isArray(data.gpus) || !Array.isArray(data.tests)) throw new Error('invalid_response');
    const count = counts(data.counts);
    const fragment = document.createDocumentFragment();
    const head = node('div', 'compat-detail-head');
    head.append(node('h3', '', data.game.name));
    if (data.game.catalog?.localizedName && data.game.catalog.localizedName !== data.game.name) head.append(node('p', 'compat-filter-label', data.game.catalog.localizedName));
    if (data.game.steamAppId) head.append(node('p', 'compat-hint', 'Steam App ID：' + data.game.steamAppId));
    fragment.append(head);
    renderHardware(data.requirements, fragment);
    renderRequirements(data.requirements, fragment);
    renderAdaptations(data.modCompatibility, fragment);
    renderCatalog(data.game, fragment);
    if (count.total > 0) renderReports(data, count, fragment);
    content.replaceChildren(fragment);
    byId('compat-detail-title').textContent = data.game.name + ' · 配置与适配';
    document.title = data.game.name + '配置要求 · AMD DLSS MU';
    message(detailState, '官方配置、专业硬件参考与上游 Mod 适配分别展示。');
  }
  function renderReports(data, count, fragment) {
    const archive = node('details', 'compat-source-box');
    archive.append(node('summary', '', '历史用户环境记录 · ' + count.total + ' 条'));
    const totals = node('dl', 'compat-totals');
    for (const [key, label] of [['total', '全部实测'], ['success', '成功'], ['partial', '部分成功'], ['failure', '失败']]) {
      const cell = node('div');
      appendPair(cell, label, count[key]);
      totals.append(cell);
    }
    archive.append(totals, node('p', 'compat-hint', '最近提交：' + time(data.lastTestedAt) + '。历史反馈只代表记录中的具体环境，未用于上方硬件估算。'));
    for (const test of data.tests.slice(0, 50)) {
      if (!test.environment?.gpu || !['success', 'partial', 'failure'].includes(test.result)) throw new Error('invalid_response');
      const env = test.environment;
      const report = node('details', 'compat-report');
      const summary = node('summary');
      summary.append(badge(test.result), node('span', 'compat-report-line', known(env.gpu.name) + ' · 驱动 ' + known(env.gpu.driverVersion)), node('span', 'compat-report-line', known(test.tester) + ' · 提交于 ' + time(test.createdAt)));
      const fields = node('dl', 'compat-environment');
      const memory = Number.isFinite(env.gpu.vramMb) && env.gpu.vramMb > 0 ? env.gpu.vramMb.toLocaleString('zh-CN') + ' MB' : '未知';
      for (const [label, value] of [
        ['测试结果', labels[test.result]], ['匿名玩家', known(test.tester)], ['提交时间', time(test.createdAt)],
        ['显卡型号', known(env.gpu.name)], ['厂商', known(env.gpu.vendor)], ['专用显存', memory], ['架构', known(env.gpu.architecture)],
        ['Windows 驱动', known(env.gpu.driverVersion)], ['操作系统', known(env.osVersion)], ['系统 DirectX', known(env.systemDirectX)],
        ['游戏版本', known(env.gameVersion)], ['游戏渲染 API', known(env.renderApi)], ['DLSS 版本', known(env.dlssVersion)],
        ['DLSS5 版本', known(env.dlss5Version)], ['MU 版本', known(env.toolVersion)], ['游戏设置', known(env.settings)], ['其他 Mod', known(env.otherMods)]
      ]) appendPair(fields, label, value);
      if (test.failureReason) appendPair(fields, '问题原因', reasons[test.failureReason] || known(test.failureReason));
      if (test.notes) appendPair(fields, '补充说明', test.notes);
      report.append(summary, fields);
      archive.append(report);
    }
    fragment.append(archive);
  }
  async function loadDetail(id, focus = false) {
    const generation = ++detailGeneration;
    detailController?.abort();
    const controller = detailController = new AbortController();
    resetDetail();
    detail.setAttribute('aria-busy', 'true');
    message(detailState, '正在加载官方配置与上游适配资料…', 'loading');
    try {
      const data = await fetchJson('/api/compatibility/games/' + encodeURIComponent(id), controller);
      if (generation !== detailGeneration) return;
      if (data.game?.id !== id) throw new Error('invalid_response');
      renderDetail(data);
      loadedDetail = data;
      share.hidden = false;
      if (focus) byId('compat-detail-title').focus({ preventScroll: false });
    } catch (error) {
      if (generation !== detailGeneration) return;
      message(detailState, errorText(error), 'error');
      detailRetry.hidden = false;
    } finally { if (generation === detailGeneration) detail.setAttribute('aria-busy', 'false'); }
  }
  async function loadHardware() {
    const generation = ++gpuGeneration;
    const note = byId('compat-gpu-state');
    const retry = byId('compat-gpu-retry');
    retry.hidden = true;
    note.textContent = '正在加载专业硬件参考库…';
    try {
      const data = await fetchJson('/assets/hardware-reference.json', new AbortController());
      if (generation !== gpuGeneration) return;
      if (!data || !Array.isArray(data.sources) || !['gpu', 'cpu'].every(key => Array.isArray(data[key]) && data[key].length <= 10000 && data[key].every(item => typeof item.name === 'string' && item.name.length <= 160))) throw new Error('invalid_response');
      hardwareDatabase = data;
      for (const key of ['gpu', 'cpu']) {
        const list = byId('compat-' + key + 's'); list.replaceChildren();
        for (const model of data[key]) { const option = node('option'); option.value = model.name; list.append(option); }
      }
      note.textContent = `已收录 ${data.gpu.length} 个显卡型号与 ${data.cpu.length} 个处理器型号。型号未知时仍可查看官方配置原文。`;
      if (loadedDetail) renderDetail(loadedDetail);
    } catch {
      if (generation !== gpuGeneration) return;
      note.textContent = '硬件参考库暂时无法加载；官方游戏要求与适配资料仍可查询。';
      retry.hidden = false;
    }
  }
  function restoreUrl() {
    const params = new URLSearchParams(location.search);
    query.value = (params.get('q') || '').slice(0, 200);
    for (const [key, input] of Object.entries(profileInputs)) {
      const value = (params.get(key) || '').slice(0, input.type === 'number' ? 12 : input.maxLength);
      input.value = value;
      if (input.type === 'number' && value && !input.checkValidity()) input.value = '';
    }
    const requestedPage = Number(params.get('page') || 1);
    void search({ game: (params.get('game') || '').slice(0, 64), page: Number.isSafeInteger(requestedPage) && requestedPage >= 1 && requestedPage <= 1000000 ? requestedPage : 1, historyMode: 'replace' });
  }
  form.addEventListener('submit', event => { event.preventDefault(); void search({ game: query.value.trim() === state.q ? state.game : '' }); });
  for (const input of Object.values(profileInputs)) input.addEventListener('change', () => {
    if (!input.checkValidity()) return;
    Object.assign(state, readProfile()); writeUrl('replace');
    if (loadedDetail) renderDetail(loadedDetail);
  });
  searchRetry.addEventListener('click', () => void search({ game: state.game, page: state.page }));
  byId('compat-prev').addEventListener('click', () => void search({ page: Math.max(1, state.page - 1), focusCatalog: true }));
  byId('compat-next').addEventListener('click', () => void search({ page: Math.min(pageCount, state.page + 1), focusCatalog: true }));
  function goToPage() {
    const input = byId('compat-page-number');
    if (input.reportValidity()) void search({ page: Math.min(pageCount, Math.max(1, Number(input.value) || 1)), focusCatalog: true });
  }
  byId('compat-page-go').addEventListener('click', goToPage);
  byId('compat-page-number').addEventListener('keydown', event => { if (event.key === 'Enter') { event.preventDefault(); goToPage(); } });
  detailRetry.addEventListener('click', () => void loadDetail(state.game, true));
  byId('compat-gpu-retry').addEventListener('click', () => void loadHardware());
  window.addEventListener('popstate', restoreUrl);
  share.addEventListener('click', async () => {
    const link = location.href;
    const generation = detailGeneration;
    try {
      if (!navigator.clipboard || !window.isSecureContext) throw new Error('clipboard_unavailable');
      await navigator.clipboard.writeText(link);
      if (generation !== detailGeneration || link !== location.href) return;
      byId('compat-share-state').textContent = '已复制当前游戏与电脑配置的查询链接。';
    } catch {
      if (generation !== detailGeneration || link !== location.href) return;
      byId('compat-share-fallback').hidden = false;
      const input = byId('compat-share-url'); input.value = link; input.focus(); input.select();
      byId('compat-share-state').textContent = '请复制下方已选中的链接。';
    }
  });
  restoreUrl();
  void loadHardware();
})();
