import { createHash } from 'node:crypto';

// Canonicals never depend on an incoming Host header or tracking parameters.
export const siteOrigin = 'https://amd-dlss-mu.claude-api.cn';
const repository = 'https://github.com/xiarongwu123/AMD-DLSS-MU';
export const publicPages = new Map([
  ['/', { file: 'index.html', name: '官网', title: 'AMD DLSS MU 官网｜DLSS5（大力水手5）配置助手', description: 'AMD DLSS MU 是独立第三方 Windows 游戏图形配置助手，提供 DLSS5（大力水手5）相关 DLSS-NR 方案、OptiScaler、大力喜鹊、配置恢复、正式版下载与游戏兼容查询。' }],
  ['/dlss5', { file: 'dlss5.html', name: 'DLSS5 / 大力水手5', title: 'DLSS5（大力水手5）是什么？AMD 显卡配置与下载指南｜AMD DLSS MU', description: '了解 DLSS5、大力水手5、DLSS-NR 与 AMD DLSS MU 的关系，查阅 AMD 显卡使用条件、安装步骤、恢复方法及游戏兼容资料，并进入官网下载。' }],
  ['/download', { file: 'download.html', name: '下载', title: 'DLSS5 / 大力水手5 配置工具下载｜AMD DLSS MU 正式版', description: '下载 AMD DLSS MU Windows x64 正式版，管理 DLSS5 / 大力水手5 相关 DLSS-NR 配置、OptiScaler 与大力喜鹊。官网直下载，提供版本公告与 SHA-256 校验。' }],
  ['/guide', { file: 'guide.html', name: '使用教程', title: 'DLSS5（大力水手5）安装与恢复教程｜AMD DLSS MU', description: 'AMD DLSS MU 的 DLSS5 / 大力水手5 相关 DLSS-NR 配置教程：添加游戏、选择模式、开启 FSR、End 面板、恢复配置及常见问题，附操作视频。' }],
  ['/compatibility', { file: 'compatibility.html', name: '游戏配置与兼容查询', title: '游戏配置与显卡兼容查询｜DLSS5 使用前参考 · AMD DLSS MU', description: '查询 2,000+ 款游戏的 Steam 官方配置，比较显卡、CPU、内存并查阅 OptiScaler 适配资料。硬件估算与上游资料不代表 DLSS5、大力水手5 或 MU 已实测兼容。' }],
  ['/feedback', { file: 'feedback.html', name: '用户反馈', title: 'DLSS5 配置问题与使用反馈｜AMD DLSS MU', description: '提交 AMD DLSS MU 下载、DLSS-NR 配置、游戏启动和恢复问题，提供游戏版本、显卡、驱动及诊断信息，帮助定位实际使用问题。' }],
  ['/survey', { file: 'survey.html', name: 'Pro 功能调研', title: 'Pro 功能需求调研｜AMD DLSS MU', description: '匿名参与 AMD DLSS MU Pro 功能需求调研，选择希望优先开发的功能。问卷中的功能处于规划阶段，不代表已经上线。' }]
]);

export const dlssFaq = [
  ['DLSS5 和大力水手5 是什么关系？', '本页将「DLSS5」「DLSS 5」「大力水手5」「大力水手 5」作为同一技术话题的不同搜索写法。NVIDIA 官方名称为 DLSS 5；AMD DLSS MU 是提供相关第三方方案配置入口的独立工具。'],
  ['AMD 显卡可以通过 MU 使用 DLSS5 吗？', 'MU 提供 DLSS-NR-on-AMD 第三方方案的配置入口。能否运行取决于显卡架构、驱动、游戏渲染接口和 FSR 支持等条件。请先阅读使用教程及上游要求，再在具体游戏中验证；不能把安装成功当作兼容证明。'],
  ['从哪里下载大力水手5 相关配置工具？', '从本站「下载」页获取 AMD-DLSS-MU.exe 正式版，并核对该页面提供的 SHA-256。MU 是配置助手，不是 NVIDIA 官方 DLSS 安装包；第三方组件与原始发布信息可从页面中的项目来源链接查阅。'],
  ['DLSS-NR、OptiScaler 和大力喜鹊有什么区别？', 'DLSS-NR 是神经渲染相关方案；OptiScaler 是超分与帧生成配置方案；大力喜鹊提供独立窗口缩放。三者用途与适配条件不同，窗口缩放或帧生成生效不代表 DLSS5 神经渲染生效。'],
  ['兼容查询显示达到推荐配置，就能开启 DLSS5 吗？', '不能据此判断。查询页分别提供游戏官方硬件要求、保守硬件估算和 OptiScaler 上游适配资料。达到推荐配置不等于 MU、DLSS5 或大力水手5 已通过实测，资料不足时保留未知。'],
  ['出现「请先恢复此游戏」、闪退或 End 面板无反应怎么办？', '先退出游戏，在 MU 中对目标游戏执行「恢复配置」，再按教程检查模式与 FSR 设置。不要反复叠加安装。仍有问题时，提供游戏、显卡、驱动版本和经过检查的诊断信息，向用户反馈页提交问题。']
];

const escapeHtml = value => String(value).replace(/[&<>"']/g, char => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[char]);

export function canonicalRedirect(pathname) {
  for (const [path, page] of publicPages) {
    if (pathname === `/${page.file}` || (path !== '/' && pathname === `${path}/`)) return path;
  }
  return null;
}

export function sitemapXml() {
  // Omit lastmod rather than claim that every deploy updated every page.
  return `<?xml version="1.0" encoding="UTF-8"?>\n<urlset xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">\n${[...publicPages.keys()].map(path => `  <url><loc>${siteOrigin}${path}</loc></url>`).join('\n')}\n</urlset>\n`;
}

export function renderSeoPage(source, pathname, verification = {}) {
  const page = publicPages.get(pathname);
  if (!page) return { html: source, scriptHash: null };
  const url = `${siteOrigin}${pathname}`;
  const graph = [{ '@type': 'WebPage', '@id': `${url}#webpage`, url, name: page.title,
    description: page.description, inLanguage: 'zh-CN', isPartOf: { '@id': `${siteOrigin}/#website` } }];
  if (pathname === '/') {
    graph.push({ '@type': 'WebSite', '@id': `${siteOrigin}/#website`, url: `${siteOrigin}/`, name: 'AMD DLSS MU', alternateName: 'AMD-DLSS-MU', inLanguage: 'zh-CN' });
    graph.push({ '@type': 'SoftwareApplication', '@id': `${siteOrigin}/#software`, name: 'AMD DLSS MU',
      alternateName: 'AMD-DLSS-MU', applicationCategory: 'UtilitiesApplication', operatingSystem: 'Windows 10 / 11 x64',
      url: `${siteOrigin}/`, downloadUrl: `${siteOrigin}/download`, sameAs: repository,
      description: page.description, author: { '@type': 'Person', name: 'codeXia' },
      featureList: ['游戏库管理', 'DLSS-NR 配置', 'OptiScaler 配置', '大力喜鹊窗口缩放', '配置恢复', '诊断导出'] });
  } else {
    graph.push({ '@type': 'BreadcrumbList', itemListElement: [
      { '@type': 'ListItem', position: 1, name: 'AMD DLSS MU 官网', item: `${siteOrigin}/` },
      { '@type': 'ListItem', position: 2, name: page.name, item: url }
    ] });
  }
  if (pathname === '/dlss5') {
    graph[0]['@type'] = 'FAQPage';
    graph[0].mainEntity = dlssFaq.map(([name, text]) => ({ '@type': 'Question', name, acceptedAnswer: { '@type': 'Answer', text } }));
    source = source.replace('<!-- DLSS5_FAQ -->', dlssFaq.map(([question, answer], index) =>
      `<section id="faq-${index + 1}" class="seo-faq"><h3>${escapeHtml(question)}</h3><p>${escapeHtml(answer)}</p></section>`).join('\n'));
  }
  const json = JSON.stringify({ '@context': 'https://schema.org', '@graph': graph }).replace(/</g, '\\u003c');
  const scriptHash = createHash('sha256').update(json).digest('base64');
  const meta = [
    `<title>${escapeHtml(page.title)}</title>`,
    `<meta name="description" content="${escapeHtml(page.description)}">`,
    '<meta name="robots" content="index,follow,max-image-preview:large,max-snippet:-1,max-video-preview:-1">',
    `<link rel="canonical" href="${url}">`,
    '<meta property="og:type" content="website">',
    '<meta property="og:site_name" content="AMD DLSS MU">',
    '<meta property="og:locale" content="zh_CN">',
    `<meta property="og:title" content="${escapeHtml(page.title)}">`,
    `<meta property="og:description" content="${escapeHtml(page.description)}">`,
    `<meta property="og:url" content="${url}">`,
    `<meta property="og:image" content="${siteOrigin}/assets/logo.png">`,
    '<meta property="og:image:alt" content="AMD DLSS MU 项目标志">',
    '<meta name="twitter:card" content="summary">',
    `<meta name="twitter:title" content="${escapeHtml(page.title)}">`,
    `<meta name="twitter:description" content="${escapeHtml(page.description)}">`,
    `<meta name="twitter:image" content="${siteOrigin}/assets/logo.png">`,
    '<link rel="alternate" type="text/plain" href="/llms.txt" title="项目资料索引">',
    `<script type="application/ld+json">${json}</script>`
  ];
  for (const [name, value] of [['google-site-verification', verification.google], ['baidu-site-verification', verification.baidu]]) {
    if (value) meta.push(`<meta name="${name}" content="${escapeHtml(value)}">`);
  }
  const html = source.replace(/<title>[^<]*<\/title>/i, '')
    .replace(/<meta\s+name="description"\s+content="[^"]*"\s*\/?\s*>/i, '')
    .replace('</head>', `  ${meta.join('\n  ')}\n</head>`)
    .replace('<div class="footer-links">', '<div class="footer-links"><a href="/dlss5">DLSS5 / 大力水手5</a>')
    .replace(/\/assets\/app\.(css|js)\?v=[^"\s]+/g, '/assets/app.$1?v=20260928-seo1');
  return { html, scriptHash };
}
