(() => {
  const loginPanel = document.querySelector('[data-login-panel]');
  const workspace = document.querySelector('[data-workspace]');
  const previewPanel = document.querySelector('[data-preview-panel]');
  const status = document.querySelector('[data-status]');
  const confirm = document.querySelector('[data-confirm]');
  const publish = document.querySelector('[data-publish]');
  const logout = document.querySelector('[data-logout]');
  let preview = null;
  let publishing = false;
  let pollTimer;
  let watching = false;

  function lockPublishing(value) {
    publishing = value;
    document.querySelector('[data-preview-form] button').disabled = value;
    document.querySelector('[name="tag"]').disabled = value;
    confirm.disabled = value;
    publish.disabled = value || !preview || !confirm.checked;
  }

  function renderJob(job) {
    if (!watching) return;
    if (!job) { lockPublishing(false); message('同步任务已中断，请检查当前版本后重试发布。', 'error'); return; }
    if (job.state === 'running') {
      lockPublishing(true);
      const phases = { preparing: '准备同步', downloading: '下载到服务器', verifying: '校验安装包', ready: '安装包已就绪', publishing: '切换下载版本' };
      message(`${job.tag} · ${phases[job.phase] || job.phase} · ${(job.bytes / 1048576).toFixed(1)} / ${(job.total / 1048576).toFixed(1)} MiB。完成前旧版本仍可下载，可关闭页面后再回来查看。`);
      pollTimer = setTimeout(pollJob, 2000);
    } else {
      lockPublishing(false);
      if (job.state === 'complete') {
        showRelease(job.release); preview = null; previewPanel.hidden = true; confirm.checked = false; publish.disabled = true;
        message(`${job.tag} 已同步到服务器并通过 SHA-256 校验，官网直下载已生效。`, 'success');
        window.dispatchEvent(new Event('mu:release-updated'));
      } else message(job.message || '同步失败，旧版本保持不变。', 'error');
    }
  }

  async function pollJob() {
    clearTimeout(pollTimer);
    try { const result = await api('/api/admin/release-status'); if (watching) renderJob(result.job); }
    catch (error) {
      if (!watching) return;
      if (error.status === 401) { watching = false; lockPublishing(false); await refresh(); return; }
      message('暂时无法读取同步进度；服务器任务可能仍在继续，正在重试查询。', 'error');
      pollTimer = setTimeout(pollJob, 5000);
    }
  }

  function message(text, kind = '') {
    status.textContent = text;
    status.className = `admin-status ${kind}`;
  }

  async function api(path, payload) {
    const response = await fetch(path, {
      method: payload === undefined ? 'GET' : 'POST',
      headers: payload === undefined ? {} : { 'Content-Type': 'application/json' },
      body: payload === undefined ? undefined : JSON.stringify(payload),
      cache: 'no-store'
    });
    const result = await response.json();
    if (!response.ok) throw Object.assign(new Error(result.message || '操作失败，请稍后重试。'), { status: response.status });
    return result;
  }

  function showRelease(release) {
    document.querySelector('[data-current-version]').textContent = release.tag;
    document.querySelector('[data-current-size]').textContent = release.sizeDisplay;
    document.querySelector('[data-current-date]').textContent = release.publishedAt;
    document.querySelector('[data-current-hash]').textContent = release.sha256;
    const link = document.querySelector('[data-current-url]');
    link.href = release.downloadUrl;
    link.textContent = release.downloadUrl;
  }

  async function refresh() {
    try {
      const result = await api('/api/admin/session');
      if (!result.ok) throw new Error('not signed in');
      loginPanel.hidden = true;
      workspace.hidden = false;
      logout.hidden = false;
      showRelease(result.release);
      window.dispatchEvent(new CustomEvent('mu:admin-session', { detail: result }));
      try {
        const status = await api('/api/admin/release-status');
        if (status.job) { watching = true; clearTimeout(pollTimer); renderJob(status.job); }
      } catch { message('已登录，但暂时无法查询安装包同步状态。请稍后刷新。', 'error'); }
    } catch {
      loginPanel.hidden = false;
      workspace.hidden = true;
      logout.hidden = true;
      watching = false; clearTimeout(pollTimer); lockPublishing(false);
      window.dispatchEvent(new CustomEvent('mu:admin-session', { detail: { ok: false } }));
    }
  }

  document.querySelector('[data-login-form]').addEventListener('submit', async event => {
    event.preventDefault();
    if (publishing) return;
    const form = event.currentTarget;
    const button = form.querySelector('button');
    button.disabled = true;
    message('正在验证登录…');
    try {
      await api('/api/admin/login', { password: form.elements.password.value });
      form.reset();
      message('登录成功。', 'success');
      await refresh();
    } catch (error) { message(error.message, 'error'); }
    finally { button.disabled = false; }
  });

  document.querySelector('[data-preview-form]').addEventListener('submit', async event => {
    event.preventDefault();
    if (publishing) return;
    const form = event.currentTarget;
    const button = form.querySelector('button');
    button.disabled = true;
    preview = null;
    previewPanel.hidden = true;
    confirm.checked = false;
    publish.disabled = true;
    message('正在核对 GitHub Release…');
    try {
      const result = await api('/api/admin/preview', { tag: form.elements.tag.value.trim() });
      preview = result.release;
      document.querySelector('[data-preview-version]').textContent = preview.tag;
      document.querySelector('[data-preview-size]').textContent = `${preview.sizeDisplay} · ${preview.publishedAt}`;
      document.querySelector('[data-preview-hash]').textContent = preview.sha256;
      document.querySelector('[data-preview-url]').href = preview.releaseUrl;
      previewPanel.hidden = false;
      message('Release 信息已核对；确认发布后，服务器将下载并校验完整安装包。', 'success');
    } catch (error) { message(error.message, 'error'); }
    finally { button.disabled = false; }
  });

  document.querySelector('[name="tag"]').addEventListener('input', () => {
    preview = null;
    previewPanel.hidden = true;
    confirm.checked = false;
    publish.disabled = true;
  });
  confirm.addEventListener('change', () => { publish.disabled = !preview || !confirm.checked; });

  publish.addEventListener('click', async () => {
    if (!preview || !confirm.checked || publishing) return;
    lockPublishing(true);
    message('正在创建服务器同步任务…');
    try {
      const result = await api('/api/admin/release', { tag: preview.tag });
      watching = true; clearTimeout(pollTimer); renderJob(result.job);
    } catch (error) {
      if (error.status && error.status !== 409 && error.status < 500) { lockPublishing(false); message(error.message, 'error'); return; }
      message(`${error.message} 正在检查服务器任务状态…`, 'error');
      watching = true; pollJob();
    }
  });

  logout.addEventListener('click', async () => {
    watching = false; clearTimeout(pollTimer);
    try { await api('/api/admin/logout', {}); }
    finally { preview = null; previewPanel.hidden = true; message('已退出登录。'); await refresh(); }
  });
  window.muAdmin = { api, refresh };
  refresh();
})();
