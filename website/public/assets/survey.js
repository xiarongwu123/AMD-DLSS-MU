(() => {
  const $ = selector => document.querySelector(selector);
  const form = $('[data-survey-form]');
  const result = $('[data-survey-result]');
  const button = $('[data-survey-submit]');
  let definition;
  let saved = false;
  let pending = false;
  const selected = () => [...form.querySelectorAll('[name="features"]:checked')].map(input => input.value);
  function option(type, name, value, title, description = '') {
    const label = document.createElement('label');
    label.className = 'survey-option';
    const input = document.createElement('input');
    input.type = type; input.name = name; input.value = value;
    const content = document.createElement('span');
    const heading = document.createElement('strong');
    heading.textContent = title; content.append(heading);
    if (description) { const note = document.createElement('small'); note.textContent = description; content.append(note); }
    label.append(input, content);
    return label;
  }
  function updatePriority(value = form.querySelector('[name="priority"]:checked')?.value || '') {
    const features = selected();
    const none = features.includes('none');
    $('[data-survey-priority-block]').hidden = none;
    $('[data-survey-count]').textContent = none ? '已选择：暂时没有感兴趣的功能' : `已选 ${features.length} 项`;
    $('[data-survey-priority-hint]').textContent = features.length ? '单选一项，帮助我们明确开发优先级。' : '请先选择上面的功能，再从中选出最期待的一项。';
    $('[data-survey-priorities]').replaceChildren(...definition.features.filter(item => features.includes(item.id)).map(item => {
      const label = option('radio', 'priority', item.id, item.name);
      label.querySelector('input').checked = item.id === value;
      label.querySelector('input').required = true;
      return label;
    }));
  }
  async function request(options) {
    const response = await fetch('/api/survey', { cache: 'no-store', credentials: 'same-origin', signal: AbortSignal.timeout(15000), ...options });
    const data = await response.json();
    if (!response.ok) throw new Error(data.message || '服务暂时不可用，请稍后重试。');
    return data;
  }
  async function load() {
    $('[data-survey-retry]').hidden = true;
    $('[data-survey-state]').textContent = '正在加载问卷…';
    try {
      const data = await request();
      definition = data.definition;
      $('[data-survey-features]').replaceChildren(...definition.features.map(item => option('checkbox', 'features', item.id, item.name, item.description)));
      $('[data-survey-payments]').replaceChildren(...definition.payments.map(item => {
        const label = option('radio', 'payment', item.id, item.name);
        label.querySelector('input').required = true; return label;
      }));
      if (data.response) {
        saved = true;
        form.querySelectorAll('[name="features"]').forEach(input => { input.checked = data.response.features.includes(input.value); });
        form.elements.payment.value = data.response.payment;
        form.elements.suggestion.value = data.response.suggestion;
      }
      updatePriority(data.response?.priority);
      $('[data-survey-chars]').textContent = `${form.elements.suggestion.value.length} / 1000`;
      button.textContent = saved ? '保存我的修改 →' : '提交我的期待 →';
      $('[data-survey-state]').textContent = saved ? '已载入你之前的回答。你可以修改后重新保存，不会重复计票。' : '你的选择仅用于需求调研，不会影响现有免费功能的使用。';
      form.hidden = false;
    } catch {
      $('[data-survey-state]').textContent = '问卷暂时加载失败，请检查网络后重试。';
      $('[data-survey-retry]').hidden = false;
    }
  }
  form.addEventListener('change', event => {
    if (event.target.name === 'features') {
      if (event.target.checked) {
        if (event.target.value === 'none') form.querySelectorAll('[name="features"]:not([value="none"])').forEach(input => { input.checked = false; });
        else form.querySelector('[value="none"]').checked = false;
      }
      updatePriority();
    }
    result.textContent = '';
  });
  form.elements.suggestion.addEventListener('input', () => {
    $('[data-survey-chars]').textContent = `${form.elements.suggestion.value.length} / 1000`;
    result.textContent = '';
  });
  form.addEventListener('submit', async event => {
    event.preventDefault();
    if (pending) return;
    const features = selected();
    const priority = form.querySelector('[name="priority"]:checked')?.value || '';
    if (!features.length || (!features.includes('none') && !priority)) {
      result.textContent = '请至少选择一个功能或“暂时没有”，并从已选功能中选出最期待的一项。';
      result.dataset.tone = 'error'; result.focus(); return;
    }
    const payload = { version: definition.version, features, priority, payment: form.elements.payment.value, suggestion: form.elements.suggestion.value };
    pending = true; button.disabled = true; button.textContent = '正在保存…';
    result.textContent = ''; delete result.dataset.tone;
    // Freeze inputs while saving so the success message describes the visible answer.
    const controls = [...form.querySelectorAll('input,textarea')];
    controls.forEach(input => { input.disabled = true; });
    try {
      await request({ method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify(payload) });
      saved = true;
      result.dataset.tone = 'success';
      result.textContent = '已保存，感谢你参与决定下一步！你可以继续修改，本浏览器始终只保留一份答卷。';
    } catch (error) {
      result.dataset.tone = 'error';
      result.textContent = `${error.name === 'TimeoutError' || error.name === 'TypeError' ? '网络异常，暂时无法确认保存结果。' : error.message} 内容已保留，可重新提交；重试不会重复计票。`;
    } finally {
      pending = false; controls.forEach(input => { input.disabled = false; });
      button.disabled = false; button.textContent = saved ? '保存我的修改 →' : '提交我的期待 →'; result.focus();
    }
  });
  $('[data-survey-retry]').addEventListener('click', load);
  load();
})();
