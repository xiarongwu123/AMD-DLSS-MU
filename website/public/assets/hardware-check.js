(function (root) {
  'use strict';
  const normalize = value => String(value || '').toLowerCase().replace(/\b(?:amd|nvidia|geforce|radeon|intel|core|processor|graphics|gpu|cpu)\b/g, '').replace(/[®™]/g, '').replace(/[^a-z0-9]/g, '');
  const mobile = value => /laptop|mobile|notebook|max[ -]?q|笔记本|移动版|\b(?:gtx|rtx|rx)\s*\d{3,4}\s*(?:ti|super)?m\b/i.test(value);
  const ambiguousMemory = /^(?:gt1030|gtx1050|gtx1060|gtx1650|rtx2060|rtx3050|rtx3060|rtx3080|rtx4060ti|rtx5060ti|rx550|rx5500xt|rx6500xt|rx580|rx560|rx570|rx9060xt|arca770)$/;
  const positive = (value, maximum) => Number.isFinite(Number(value)) && Number(value) > 0 && Number(value) <= maximum ? Number(value) : null;
  function matchHardware(value, models, allowShorthand = true) {
    if (!value || mobile(value)) return null;
    const input = normalize(value);
    if (ambiguousMemory.test(input)) return null;
    const matches = models.filter(model => normalize(model.name) === input || model.id === value);
    if (matches.length === 1) return matches[0];
    const memory = String(value).match(/\b(\d+)\s*GB\b/i);
    if (memory) {
      const base = input.replace(new RegExp(memory[1] + 'gb$'), '');
      const qualified = models.filter(model => normalize(model.name) === base && model.vramMb === Number(memory[1]) * 1024);
      if (qualified.length === 1) return qualified[0];
    }
    if (allowShorthand) {
      const aliases = models.filter(model => normalize(model.name).replace(/^(?:gtx|rtx|gt|rx|arc|ryzen[3579]|i[3579]|ultra[3579])/, '') === input);
      if (aliases.length === 1) return aliases[0];
    }
    return null;
  }
  function requirementDetails(text, models) {
    if (!text || mobile(text)) return { models: [], unresolved: true, memoryMb: null };
    const source = String(text).replace(/[®™]/g, ' ').normalize('NFKC').replace(/[‐‑–—−]/g, '-');
    // Tokens retain suffixes and memory variants, preventing 4070 from matching 4070 Ti.
    const gpuMemory = '(?:\\s*[-,]?\\s*\\(?\\s*(?:VRAM\\s*:?[ ]*)?\\d+\\s*GB(?:\\s*VRAM)?\\s*\\)?)?';
    const tokens = [...source.matchAll(new RegExp('(?:NVIDIA\\s+)?(?:GeForce\\s+)?(?:GTX|RTX|GT)[\\s-]*\\d{3,4}(?:[\\s-]*(?:Ti|Super)){0,2}' + gpuMemory + '(?:\\s*\\(?\\s*(?:GDDR\\d|DDR4)\\s*\\)?)?' +
      '|(?:AMD\\s+)?(?:Radeon\\s+)?(?:RX[\\s-]*(?:Vega\\s*)?\\d{2,4}(?:[\\s-]*(?:XTX|XT|GRE))?|R9\\s*(?:\\d{3}X?|Fury\\s*X)|Radeon\\s+VII)' + gpuMemory +
      '|(?:Intel\\s+)?Arc\\s*[AB]\\d{3}' + gpuMemory +
      '|(?:AMD\\s+)?Ryzen\\s*[3579][\\s-]*\\d{4}[a-zA-Z0-9]*(?:[\\s-]+(?:X3D|XT|GE|HX|HS|X|G|F|H|U)\\b)?' +
      '|(?:Intel\\s+)?(?:Core\\s*)?i[3579][ -]?\\d{4,5}[a-zA-Z]*(?:[\\s-]+(?:KF|KS|XE|HX|HK|HQ|K|F|T|X|S|H|U)\\b)?' +
      '|(?:Intel\\s+)?Core\\s+Ultra\\s*[3579]\\s*\\d{3}[a-zA-Z]*(?:\\s+Plus)?', 'gi'))];
    const found = [];
    const explicitMemoryIds = [];
    let unresolved = !tokens.length;
    let remaining = source;
    for (const token of tokens) {
      const value = token[0].trimEnd();
      const tail = source.slice(token.index + value.length);
      const prefix = source.slice(0, token.index);
      const partial = /[a-z0-9]$/i.test(prefix) || /^[a-z0-9]/i.test(tail) || /^[\s-]*(?:Ti|Super|XT|XTX|X3D|GRE|GDDR\d|DDR4|\d+\s*GB)\b/i.test(tail);
      const exact = partial ? null : matchHardware(value.replace(/\bVRAM\b/gi, ''), models, false);
      if (exact && !found.some(item => item.id === exact.id)) found.push(exact);
      if (exact && /\d+\s*GB\b/i.test(value)) explicitMemoryIds.push(exact.id);
      if (!exact) unresolved = true;
      remaining = remaining.replace(token[0], ' ');
    }
    // Memory written outside a model token is a separate requirement, not that model's variant.
    const capacities = [...remaining.matchAll(/(?:\b(\d+(?:\.\d+)?)\s*(GB|MB)\s*(?:of\s+)?(?:VRAM|video\s*(?:RAM|memory)|显存)|(?:VRAM|video\s*(?:RAM|memory)|显存)\s*[:：]?\s*(\d+(?:\.\d+)?)\s*(GB|MB))/gi)].map(match => Number(match[1] || match[3]) * ((match[2] || match[4]).toUpperCase() === 'GB' ? 1024 : 1));
    return { models: found, unresolved, memoryMb: capacities.length ? Math.max(...capacities) : null, explicitMemoryIds };
  }
  function requirementsModels(text, models) {
    return requirementDetails(text, models).models;
  }
  function directComparison(actual, reference, margin = 1.1) {
    const valid = score => score && Number.isFinite(score.min) && Number.isFinite(score.max) && score.min > 0 && score.max >= score.min;
    const shared = Object.keys(actual.scores || {}).filter(id => valid(actual.scores[id]) && valid(reference.scores?.[id]));
    if (!shared.length) return null;
    const above = shared.every(id => actual.scores[id].min >= reference.scores[id].max * margin);
    const below = shared.every(id => actual.scores[id].max * margin <= reference.scores[id].min);
    return { relation: above ? 'higher' : below ? 'lower' : 'close', sourceIds: shared, method: 'same-suite' };
  }
  function compareModels(actual, reference, models) {
    if (actual.id === reference.id) return { relation: 'same', method: 'same-model', sourceIds: Object.keys(actual.scores) };
    const direct = directComparison(actual, reference);
    if (direct) return direct;
    // No scores are normalized across suites. A single shared card can only establish
    // a conservative ordering when both separately measured gaps exceed 25%.
    const inferences = [];
    for (const bridge of models) {
      const first = directComparison(actual, bridge, 1.25);
      const second = directComparison(bridge, reference, 1.25);
      if (first && second && first.relation === second.relation && ['higher', 'lower'].includes(first.relation))
        inferences.push({ relation: first.relation, method: 'shared-model-inference', bridge: bridge.name, sourceIds: [...new Set([...first.sourceIds, ...second.sourceIds])] });
    }
    if (inferences.length && inferences.every(item => item.relation === inferences[0].relation)) return inferences[0];
    return { relation: 'unknown', method: 'no-comparable-measurement', sourceIds: [] };
  }
  function component(kind, supplied, required, database, vramGb) {
    const label = kind === 'gpu' ? '显卡' : '处理器';
    if (!required) return { key: kind, label, status: 'unknown', text: '发行商未提供可比对的' + label + '要求。', sourceIds: [] };
    if (!supplied) return { key: kind, label, status: 'unknown', text: '选择你的' + label + '后对比。', sourceIds: [] };
    const actual = matchHardware(supplied, database[kind]);
    const parsed = requirementDetails(required, database[kind]);
    const references = parsed.models;
    if (!actual || !references.length) return { key: kind, label, status: 'unknown', text: !actual ? '该型号或显存版本尚无明确基准对应，请查看下方官方要求。' : '官方要求中的型号尚无可比基准，请查看下方原文。', sourceIds: [] };
    const comparisons = references.map(reference => {
      const comparison = compareModels(actual, reference, database[kind]);
      const suppliedMemory = positive(vramGb, 1024);
      const memory = actual.vramMb ?? (suppliedMemory ? suppliedMemory * 1024 : null);
      const requiredMemory = reference.vramMb == null ? parsed.memoryMb : Math.max(reference.vramMb, parsed.memoryMb || 0);
      const explicitMemory = parsed.memoryMb != null || parsed.explicitMemoryIds.includes(reference.id);
      let status = ['same', 'higher'].includes(comparison.relation) ? 'pass' : comparison.relation === 'lower' ? 'fail' : 'unknown';
      if (kind === 'gpu' && status === 'pass') {
        if (memory == null || requiredMemory == null) status = 'unknown';
        else if (memory < requiredMemory) status = explicitMemory ? 'fail' : 'unknown';
      }
      return { ...comparison, status, reference, memory, requiredMemory, explicitMemory };
    });
    const best = comparisons.find(item => item.status === 'pass') || comparisons.find(item => item.status === 'unknown') || comparisons[0];
    // Recognized alternatives can prove sufficiency; incomplete parsing cannot prove failure.
    const unknownAlternative = /\b(?:or|and)\b|\/|或/i.test(required) && (parsed.unresolved || references.length < 2);
    const verdict = best.status === 'fail' && unknownAlternative ? 'unknown' : best.status;
    const relationText = best.relation === 'same' ? '与官方参考型号一致' : best.relation === 'higher' ? '综合游戏性能高于参考型号（估算）' : best.relation === 'lower' ? '综合游戏性能低于参考型号（估算）' : '差距接近或缺少可比数据，暂不作通过判断';
    const memoryText = kind === 'gpu' ? (best.memory == null ? '；你的显存容量未确认' : best.requiredMemory == null ? '；参考显卡的显存容量未确认' : best.memory < best.requiredMemory ? (best.explicitMemory ? `；显存 ${best.memory / 1024} GB，低于所需 ${best.requiredMemory / 1024} GB` : `；显存 ${best.memory / 1024} GB，参考型号配备 ${best.requiredMemory / 1024} GB；官方未明确最低显存，暂不能确认`) : `；显存 ${best.memory / 1024} GB，不低于${best.explicitMemory ? '所需' : '参考型号的'} ${best.requiredMemory / 1024} GB`) : '';
    return { key: kind, label, status: verdict, text: `${actual.name}：${relationText} ${best.reference.name}${memoryText}`,
      sourceIds: [...new Set([...best.sourceIds, ...(kind === 'gpu' ? [actual.memorySourceId, best.reference.memorySourceId].filter(Boolean) : [])])], method: best.method, ...(best.bridge ? { note: '经共享型号 ' + best.bridge + ' 作跨测试期的保守档位推断，未横比分数。' } : {}) };
  }
  function unverifiedConstraints(tier) {
    const constraints = { cpu: [], gpu: [] };
    if (!tier) return constraints;
    const processor = String(tier.processor || '');
    const graphics = String(tier.graphics || '');
    const notes = String(tier.additionalNotes || '');
    const cpuText = processor + ' ' + notes;
    const gpuText = graphics + ' ' + notes;
    if (/\b(?:\d+\s*\+?\s*[- ]?(?:(?:physical|hardware|CPU)\s+)*(?:cores?|threads?)|(?:single|dual|triple|quad|six|hexa|eight|octa|ten|deca|twelve|sixteen)[- ]cores?)\b/i.test(cpuText)) constraints.cpu.push('核心数／线程数');
    if (/\b(?:AVX(?:2|[- ]?512)?|SSE[234]?(?:\.[12])?|FMA[34]?|AES[- ]NI|NEON)\b/i.test(cpuText)) constraints.cpu.push('指令集支持');
    if (/\b(?:shader\s*model\s*[\d.]|mesh\s*shaders?|feature\s*level\s*\d)/i.test(gpuText)) constraints.gpu.push('Shader Model／硬件功能级别');
    const graphicsApi = /\b(?:DirectX|Direct3D|DX)\s*[®™]?\s*\d+(?:\.\d+)?|\b(?:Vulkan|OpenGL)\s*\d/i;
    if (graphicsApi.test(graphics) || /\b(?:GPU|graphics?\s*(?:card|hardware)|video\s*card)[^;\n]{0,100}\b(?:DirectX|Direct3D|DX|Vulkan|OpenGL)\s*[®™]?\s*\d/i.test(notes)) constraints.gpu.push('图形 API 的硬件支持');
    // An optional "required to support ray tracing" path is not a baseline requirement.
    if (/\bray[- ]?tracing(?:[- ]capable|\s+(?:support\s+)?(?:is\s+)?(?:required|mandatory))|\b(?:requires?|mandatory|must\s+support)\s+(?:hardware\s+)?ray[- ]?tracing/i.test(gpuText)) constraints.gpu.push('强制硬件光追支持');
    if (/\b(?:RTX|RX|GeForce|Radeon|RDNA)\b[^.;]{0,45}(?:series\s+required|or\s+above\s+required)|\bno\s+more\s+than\s+\d+\s+generations?\s+old\b/i.test(graphics)) constraints.gpu.push('指定显卡系列／代际');
    return constraints;
  }
  function tierChecks(tier, profile, database, baseline = { cpu: [], gpu: [] }) {
    if (!tier) return [];
    const memory = positive(profile.ramGb, 4096);
    const extra = unverifiedConstraints(tier);
    const checks = [component('gpu', profile.gpu, tier.graphics, database, profile.vramGb), component('cpu', profile.cpu, tier.processor, database),
      { key: 'ram', label: '内存', status: tier.memoryMb > 0 && memory > 0 ? (memory * 1024 >= tier.memoryMb ? 'pass' : 'fail') : 'unknown', sourceIds: [],
        text: tier.memoryMb > 0 ? (memory > 0 ? `${memory} GB / 官方要求 ${tier.memoryMb / 1024} GB` : `选择内存容量；官方要求 ${tier.memoryMb / 1024} GB`) : '官方内存要求暂不能自动解析，请查看原文。' }];
    for (const check of checks) {
      if (check.key === 'ram') continue;
      const constraints = [...new Set([...baseline[check.key], ...extra[check.key]])];
      if (!constraints.length) continue;
      check.unverifiedRequirements = constraints;
      if (check.status === 'pass') check.status = 'unknown';
      check.text += '；官方另有限定：' + constraints.join('、') + '。当前资料尚未核对这些条件，性能比较不能代替验证。';
    }
    return checks;
  }
  function evaluate(requirements, profile, database) {
    if (!requirements || requirements.status !== 'available' || !database?.gpu || !database?.cpu)
      return { status: 'unknown', title: '配置资料暂不完整', summary: '目前没有足够的官方配置资料作判断。', checks: [], sources: [] };
    const baseline = unverifiedConstraints(requirements.minimum);
    const minimum = tierChecks(requirements.minimum, profile, database, baseline);
    const recommended = tierChecks(requirements.recommended, profile, database, baseline);
    const allPass = checks => checks.length === 3 && checks.every(item => item.status === 'pass');
    let status = 'incomplete', title = '查看你的硬件与官方要求的对比', checks = minimum;
    if (minimum.some(item => item.status === 'fail')) {
      status = 'below_minimum';
      const failed = minimum.filter(item => item.status === 'fail');
      title = failed.length === 1 && failed[0].key === 'ram' ? '内存低于最低配置要求' : failed.map(item => item.label).join('、') + '低于最低配置参考（估算）';
    }
    else if (allPass(recommended)) { status = 'recommended'; title = '核心硬件预计达到推荐配置'; checks = recommended; }
    else if (allPass(minimum)) { status = 'minimum'; title = '核心硬件预计达到最低配置'; }
    else {
      const graphics = recommended.find(item => item.key === 'gpu' && item.status === 'pass') || minimum.find(item => item.key === 'gpu' && item.status === 'pass');
      if (graphics) { title = recommended.includes(graphics) ? '显卡预计达到推荐配置档位' : '显卡预计达到最低配置档位'; checks = recommended.includes(graphics) ? recommended : minimum; }
      else if (recommended.some(item => item.unverifiedRequirements?.length)) checks = recommended;
    }
    const sourceIds = new Set(checks.flatMap(item => item.sourceIds));
    return { status, title, summary: '对比范围：处理器、显卡综合游戏性能与内存。具体帧率、光追能力和 MU 功能不由此结论推定；系统、DirectX 与安装条件见官方要求。',
      checks, sources: database.sources.filter(source => sourceIds.has(source.id)) };
  }
  root.MuHardwareCheck = { normalize, matchHardware, requirementsModels, compareModels, evaluate };
})(typeof window === 'undefined' ? globalThis : window);
